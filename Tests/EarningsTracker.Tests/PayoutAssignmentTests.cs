using System.Text.Json;
using System.IO;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EarningsTracker.Tests;

[Trait("Category", "Regression")]
public class PayoutAssignmentTests
{
    private static readonly DateTime Day = new(2026, 11, 5);
    internal static Session Recording(string name = "Example", decimal earnings = 10m, SessionStatus status = SessionStatus.Verified) => new()
    {
        GameName = name, Date = Day.AddHours(15), RecordedDate = Day.AddHours(15), PostedDate = Day.AddHours(16),
        Duration = TimeSpan.FromMinutes(30), Earnings = earnings, Status = status
    };
    internal static Payout Payment(string source = "ONE", string status = "Paid", decimal amount = -10m) => new()
    { Date = Day, Type = "Payment", Status = status, Amount = amount, Description = source, SourceId = source };

    [Fact]
    public void SameDayAssignmentOverridesCutoffAndLeavesLegacyAndUndatedRulesIntact()
    {
        var payment = Payment(); payment.Id = 7;
        var current = Recording(); current.HasExplicitPayoutAssignment = true;
        var settled = Recording(); settled.HasExplicitPayoutAssignment = true; settled.AssignedPayoutId = 7;
        var legacy = Recording();
        var undated = Recording(); undated.HasExplicitPayoutAssignment = true; undated.PostedDate = null;
        var pending = Recording(status: SessionStatus.Pending); pending.HasExplicitPayoutAssignment = true;
        var totals = PayoutAccounting.Calculate([current, settled, legacy, undated, pending], [payment]);
        Assert.Equal(10m, totals.Balance);
        Assert.Equal(40m, totals.TotalEarned);
        Assert.Equal(10m, totals.TotalPaidOut);
        Assert.Equal(1, totals.UndatedVerifiedSessions);
    }

    [Fact]
    public void ManualReassignmentIsAuditedPreservesSourceAndSurvivesReimport()
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(Payment(), options: database.Options);
        var session = Recording();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([session]));
        using (var db = database.CreateContext())
        {
            var saved = db.Sessions.Single(); saved.PayoutId = payment.Id; db.SaveChanges();
        }
        session.PayoutId = payment.Id;
        session.AssignedPayoutId = payment.Id;
        SessionService.Update(session.Id, session, database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        using (var read = database.CreateContext())
        {
            var saved = read.Sessions.Single();
            Assert.Equal(payment.Id, saved.AssignedPayoutId);
            Assert.Equal(payment.Id, saved.PayoutId);
            Assert.Equal(Day.AddHours(16), saved.PostedDate);
            Assert.Equal(0m, PayoutAccounting.Calculate([saved], read.Payouts.ToList()).Balance);
            var change = read.SessionChanges.Single(c => c.ChangeType == "Updated");
            using var audit = JsonDocument.Parse(change.AfterValuesJson);
            Assert.Equal(payment.Id, audit.RootElement.GetProperty("AssignedPayoutId").GetInt32());
        }
        session.AssignedPayoutId = null;
        SessionService.Update(session.Id, session, database.Options);
        using var reopened = database.CreateContext();
        Assert.Equal(10m, PayoutAccounting.Calculate(reopened.Sessions.ToList(), reopened.Payouts.ToList()).Balance);
        Assert.Equal(payment.Id, reopened.Sessions.Single().PayoutId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalPendingRecordingVerifiedAfterPaymentDefaultsToCurrent(bool useImport)
    {
        using var database = new TestDatabase();
        var pending = Recording(status: SessionStatus.Pending);
        using (var db = database.CreateContext()) { db.Sessions.Add(pending); db.SaveChanges(); }
        PayoutService.Create(Payment(), options: database.Options);
        if (useImport)
        {
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording()]));
        }
        else { pending.Status = SessionStatus.Verified; SessionService.Update(pending.Id, pending, database.Options); }
        using var read = database.CreateContext();
        Assert.True(read.Sessions.Single().HasExplicitPayoutAssignment);
        Assert.Null(read.Sessions.Single().AssignedPayoutId);
        Assert.Equal(10m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
    }

    [Fact]
    public void CoverageCapturesOnlyReviewedSessionsAndFreezesUncheckedLegacyEarnings()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Sessions.AddRange(Recording("Covered"), Recording("Left open", 5m), Recording("Pending", status: SessionStatus.Pending));
            db.SaveChanges();
        }
        var payment = Payment();
        PayoutCoveragePreview preview;
        using (var db = database.CreateContext()) preview = PayoutAssignments.Preview(db, payment);
        preview.Sessions.Single(c => c.Earnings == 5m).IsSelected = false;
        PayoutService.Create(payment, options: database.Options, coverage: preview);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording("Verified later", 3m)]));
        using var read = database.CreateContext();
        Assert.Equal(payment.Id, read.Sessions.Single(s => s.GameName == "Covered").AssignedPayoutId);
        var uncheckedSession = read.Sessions.Single(s => s.GameName == "Left open");
        Assert.True(uncheckedSession.HasExplicitPayoutAssignment);
        Assert.Null(uncheckedSession.AssignedPayoutId);
        Assert.Equal(8m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
        Assert.Equal(2, read.SessionChanges.Count(c => c.ChangeType == "Payout assignment"));
    }

    [Fact]
    public void StaleCoverageRejectsEntirePaymentAndAllowsRepreview()
    {
        using var database = new TestDatabase();
        var payment = Payment();
        PayoutCoveragePreview preview;
        using (var db = database.CreateContext()) preview = PayoutAssignments.Preview(db, payment);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        Assert.Throws<InvalidOperationException>(() => PayoutService.Create(payment, options: database.Options, coverage: preview));
        using (var read = database.CreateContext()) { Assert.Empty(read.Payouts); Assert.Empty(read.PayoutChanges); }
        using (var db = database.CreateContext()) preview = PayoutAssignments.Preview(db, payment);
        PayoutService.Create(payment, options: database.Options, coverage: preview);
        using var saved = database.CreateContext();
        Assert.Equal(payment.Id, saved.Sessions.Single().AssignedPayoutId);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Failed")]
    [InlineData("Processing")]
    [InlineData("Paid")]
    public void AssignedPaymentStatusControlsOwedWithoutRewritingMembership(string status)
    {
        var payment = Payment(status: status); payment.Id = 8;
        var session = Recording(); session.HasExplicitPayoutAssignment = true; session.AssignedPayoutId = 8;
        Assert.Equal(status is "Paid" or "Processing" ? 0m : 10m, PayoutAccounting.Calculate([session], [payment]).Balance);
        payment.IsDeleted = true;
        Assert.Equal(10m, PayoutAccounting.Calculate([session], [payment]).Balance);
        Assert.Equal(8, session.AssignedPayoutId);
    }

    [Fact]
    public void InvalidPaymentAndStaleSessionCannotOverwriteAssignments()
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(Payment(status: "Failed"), options: database.Options);
        var importer = new SessionImporter(database.Options);
        var session = Recording(); importer.ImportSessions(importer.PreviewImport([session]));
        var expected = SessionService.CaptureEditState(session);
        session.AssignedPayoutId = payment.Id;
        Assert.Throws<ArgumentException>(() => SessionService.Update(session.Id, session, database.Options));
        session.AssignedPayoutId = null;
        session.Earnings = 11m; SessionService.Update(session.Id, session, database.Options);
        Assert.Throws<InvalidOperationException>(() => SessionService.Update(session.Id, session, database.Options, expected));
    }

    [Fact]
    public void ImportCoverageIsIdempotentAndProcessingToPaidDoesNotAbsorbLaterSessions()
    {
        using var database = new TestDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording()]));
        using var importer = new PayoutImporter(database.CreateContext());
        var parsed = new PayoutParseResult(); parsed.Payouts.Add(Payment(status: "Processing"));
        var preview = importer.PreviewImport(parsed);
        Assert.Single(Assert.Single(preview.CoveragePreviews).Sessions);
        importer.ImportPayouts(preview);
        sessions.ImportSessions(sessions.PreviewImport([Recording("Later", 2m)]));
        var paid = new PayoutParseResult(); paid.Payouts.Add(Payment(status: "Paid"));
        var update = importer.PreviewImport(paid);
        Assert.Empty(update.CoveragePreviews);
        importer.ImportPayouts(update);
        importer.ImportPayouts(importer.PreviewImport(paid));
        using var read = database.CreateContext();
        Assert.Single(read.Payouts);
        Assert.Equal(2m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
        Assert.Single(read.SessionChanges.Where(c => c.ChangeType == "Payout assignment"));
    }

    [Fact]
    public void MultipleSameDayPaymentsCannotCoverOneSessionTwice()
    {
        using var database = new TestDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording()]));
        using var importer = new PayoutImporter(database.CreateContext());
        var parsed = new PayoutParseResult(); parsed.Payouts.AddRange([Payment("First"), Payment("Second")]);
        var preview = importer.PreviewImport(parsed);
        Assert.Equal(2, preview.CoveragePreviews.Count);
        Assert.False(preview.CoveragePreviews[1].Sessions.Single().IsSelected);
        preview.CoveragePreviews[1].Sessions.Single().IsSelected = true;
        Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(preview));
        using (var read = database.CreateContext()) Assert.Empty(read.Payouts);
        preview.CoveragePreviews[0].Sessions.Single().IsSelected = false;
        importer.ImportPayouts(preview);
        using var saved = database.CreateContext();
        Assert.Equal(saved.Payouts.Single(p => p.SourceId == "Second").Id, saved.Sessions.Single().AssignedPayoutId);
    }

    [Fact]
    public void AssignmentAuditFailureRollsBackPaymentAndMembership()
    {
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        var payment = Payment();
        PayoutCoveragePreview preview;
        using (var db = database.CreateContext())
        {
            preview = PayoutAssignments.Preview(db, payment);
            db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectAssignment BEFORE INSERT ON SessionChanges WHEN NEW.ChangeType = 'Payout assignment' BEGIN SELECT RAISE(ABORT, 'synthetic audit failure'); END;");
        }
        Assert.Throws<DbUpdateException>(() => PayoutService.Create(payment, options: database.Options, coverage: preview));
        using var read = database.CreateContext();
        Assert.Empty(read.Payouts); Assert.Empty(read.PayoutChanges);
        Assert.Null(read.Sessions.Single().AssignedPayoutId);
        Assert.Single(read.SessionChanges);
    }

    [Fact]
    public void SameImportForceVerificationRemainsCurrentRatherThanJoiningItsPayment()
    {
        using var database = new TestDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording(status: SessionStatus.Pending), Recording("Already verified", 4m)]));
        using var importer = new PayoutImporter(database.CreateContext());
        var parsed = new PayoutParseResult(); parsed.Payouts.Add(Payment(amount: -4m));
        var verified = Recording(); verified.IsForceVerified = true; verified.ImportSourceText = "Force verification evidence";
        parsed.ForceVerifiedSessions.Add(verified);
        var preview = importer.PreviewImport(parsed);
        Assert.Equal(4m, Assert.Single(Assert.Single(preview.CoveragePreviews).Sessions).Earnings);
        importer.ImportPayouts(preview);
        using var read = database.CreateContext();
        var newlyVerified = read.Sessions.Single(s => s.GameName == "Example");
        Assert.True(newlyVerified.HasExplicitPayoutAssignment); Assert.Null(newlyVerified.AssignedPayoutId);
        Assert.Equal(10m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
        Assert.Equal("Force verification evidence", read.SessionChanges.Single(c => c.ChangeType == "Payout import update").SourceText);
    }

    [Fact]
    public void AssignmentOnlyEditPreservesUnknownHistoricalPostingDate()
    {
        using var database = new TestDatabase();
        var session = Recording(); session.IsForceVerified = true; session.PostedDate = null;
        using (var db = database.CreateContext()) { db.Sessions.Add(session); db.SaveChanges(); }
        var item = new ViewModels.SessionItemViewModel(session); item.BeginEdit();
        item.DraftPayout = item.PayoutOptions.Single(p => p.IsExplicit && p.PayoutId == null);
        Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
        SessionService.Update(session.Id, edited, database.Options, item.EditState);
        using var read = database.CreateContext();
        var saved = read.Sessions.Single(); Assert.Null(saved.PostedDate); Assert.True(saved.HasExplicitPayoutAssignment);
        var totals = PayoutAccounting.Calculate([saved], []);
        Assert.Equal(0m, totals.Balance); Assert.Equal(1, totals.UndatedVerifiedSessions);
    }

    [Fact]
    public void RevivingPaymentCanLeavePreviouslyAssignedSessionUncheckedAndOwed()
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(Payment(status: "Failed"), options: database.Options);
        using (var db = database.CreateContext())
        {
            var session = Recording(); session.HasExplicitPayoutAssignment = true; session.AssignedPayoutId = payment.Id;
            db.Sessions.Add(session); db.SaveChanges();
        }
        payment.Status = "Paid";
        PayoutCoveragePreview preview;
        using (var db = database.CreateContext()) preview = PayoutAssignments.Preview(db, payment);
        preview.Sessions.Single().IsSelected = false;
        PayoutService.Update(payment.Id, payment, options: database.Options, coverage: preview);
        using var read = database.CreateContext();
        Assert.Null(read.Sessions.Single().AssignedPayoutId);
        Assert.Equal(10m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
    }

    [Fact]
    public void FileBackedAssignmentsSurviveClosingAllConnectionsAndReopening()
    {
        var directory = Path.Combine(TestStorageIsolation.DirectoryPath, "payout-assignment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "synthetic.db"), ForeignKeys = true, Pooling = false }.ToString()).Options;
        try
        {
            using (var db = new AppDbContext(options)) db.Database.Migrate();
            var importer = new SessionImporter(options); importer.ImportSessions(importer.PreviewImport([Recording()]));
            var payment = Payment();
            PayoutCoveragePreview preview;
            using (var db = new AppDbContext(options)) preview = PayoutAssignments.Preview(db, payment);
            PayoutService.Create(payment, options: options, coverage: preview);
            using var reopened = new AppDbContext(options);
            Assert.Equal(payment.Id, reopened.Sessions.Single().AssignedPayoutId);
            Assert.True(reopened.Sessions.Single().HasExplicitPayoutAssignment);
            Assert.Equal(0m, PayoutAccounting.Calculate(reopened.Sessions.ToList(), reopened.Payouts.ToList()).Balance);
            Assert.Equal(2, reopened.SessionChanges.Count());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BaselineUpgradePreservesLegacyLinksDatesEarningsAndAuditAndCanRepeat()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var db = new AppDbContext(options);
        db.GetService<IMigrator>().Migrate("20261013055107_InitialCreate");
        db.Database.ExecuteSqlRaw("INSERT INTO Payouts (Id,StartDate,EndDate,Date,Type,Description,Status,Amount,IsAdjustmentIncludedInSessionEarnings,ReconciliationState,IsDeleted) VALUES (1,'2026-11-01','2026-11-05','2026-11-05','Payment','Legacy payment','Paid','-10',0,'Imported',0);");
        db.Database.ExecuteSqlRaw("INSERT INTO Sessions (GameName,PayoutId,Date,RecordedDate,PostedDate,Duration,Status,Earnings,IsForceVerified) VALUES ('Legacy',1,'2026-11-05 15:00:00','2026-11-05 15:00:00','2026-11-05 16:00:00','00:30:00',1,'1.2345',0);");
        db.Database.ExecuteSqlRaw("INSERT INTO SessionChanges (SessionId,ChangedAt,ChangeType,BeforeValuesJson,AfterValuesJson,SourceText) VALUES (1,'2026-11-05','Imported',@p0,@p1,'Original evidence');", "{}", "{}");
        db.Database.Migrate(); db.Database.Migrate();
        var session = db.Sessions.Single();
        Assert.False(session.HasExplicitPayoutAssignment); Assert.Null(session.AssignedPayoutId);
        Assert.Equal(1, session.PayoutId); Assert.Equal(1.2345m, session.Earnings);
        Assert.Equal(Day.AddHours(16), session.PostedDate);
        Assert.Equal("Original evidence", db.SessionChanges.Single().SourceText);
        Assert.Equal(0m, PayoutAccounting.Calculate([session], db.Payouts.ToList()).Balance);
        Assert.False(db.Database.HasPendingModelChanges());
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader(); Assert.False(reader.Read());
    }
}
