using System.IO;
using System.Text.Json;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace EarningsTracker.Tests;

// Separate file connections exercise SQLite's actual transaction/visibility boundary.
[Collection("DataChanged")]
[Trait("Category", "Regression")]
public class ImportConcurrencyTests
{
    private static Session Recording(decimal earnings = 1m) => new()
    {
        GameName = "Synthetic concurrency", Date = new(2026, 6, 5, 12, 0, 0),
        RecordedDate = new(2026, 6, 5, 12, 0, 0), Duration = TimeSpan.FromMinutes(1),
        Status = SessionStatus.Verified, Earnings = earnings
    };

    private static PayoutParseResult Payment(decimal amount = -10m) => new()
    {
        Payouts = { new Payout { Date = new(2026, 6, 6), Type = "Payment", Description = "Synthetic payment",
            SourceId = "PRO-11", Status = "Paid", Amount = amount } }
    };

    [Fact]
    public void SessionInsertedAfterPreviewRejectsWholeStaleBatch()
    {
        using var database = new FileDatabase();
        var importer = new SessionImporter(database.Options);
        var stale = importer.PreviewImport([Recording()]);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        Assert.Throws<InvalidOperationException>(() => importer.ImportSessions(stale));
        using var read = database.CreateContext();
        Assert.Single(read.Sessions);
        Assert.Single(read.SessionChanges);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        Assert.Single(read.SessionChanges);
    }

    [Fact]
    public void PayoutInsertedAfterPreviewRejectsWholeStaleBatch()
    {
        using var database = new FileDatabase();
        using var first = new PayoutImporter(database.CreateContext());
        using var second = new PayoutImporter(database.CreateContext());
        var stale = first.PreviewImport(Payment());
        second.ImportPayouts(second.PreviewImport(Payment()));
        Assert.Throws<InvalidOperationException>(() => first.ImportPayouts(stale));
        using var read = database.CreateContext();
        Assert.Single(read.Payouts);
        Assert.Single(read.PayoutChanges);
        using var retry = new PayoutImporter(database.CreateContext());
        retry.ImportPayouts(retry.PreviewImport(Payment()));
        Assert.Single(read.PayoutChanges);
    }

    [Fact]
    public void SessionEditedAfterPreviewRejectsStaleUpdate()
    {
        using var database = new FileDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording()]));
        var stale = importer.PreviewImport([Recording(2m)]);
        using (var writer = database.CreateContext())
        {
            var edited = writer.Sessions.Single();
            edited.Earnings = 3m;
            SessionService.Update(edited.Id, edited, database.Options);
        }
        Assert.Throws<InvalidOperationException>(() => importer.ImportSessions(stale));
        using var read = database.CreateContext();
        Assert.Equal(3m, Assert.Single(read.Sessions).Earnings);
        Assert.Equal(2, read.SessionChanges.Count());
    }

    [Fact]
    public void PayoutEditedAfterPreviewRejectsTrackedStaleUpdate()
    {
        using var database = new FileDatabase();
        using (var seed = new PayoutImporter(database.CreateContext()))
            seed.ImportPayouts(seed.PreviewImport(Payment()));
        using var importer = new PayoutImporter(database.CreateContext());
        var stale = importer.PreviewImport(Payment(-12m));
        using (var writer = database.CreateContext())
        {
            var edited = writer.Payouts.Single();
            edited.Amount = -15m;
            PayoutService.Update(edited.Id, edited, options: database.Options);
        }
        Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(stale));
        using var read = database.CreateContext();
        Assert.Equal(-15m, Assert.Single(read.Payouts).Amount);
        Assert.Equal(2, read.PayoutChanges.Count());
    }

    [Fact]
    public void ForceVerificationTargetChangedAfterPreviewRejectsMixedBatch()
    {
        using var database = new FileDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording()]));
        using var importer = new PayoutImporter(database.CreateContext());
        var parsed = Payment();
        var forceVerified = Recording(2m);
        forceVerified.PostedDate = new(2026, 6, 6);
        parsed.ForceVerifiedSessions.Add(forceVerified);
        var stale = importer.PreviewImport(parsed);
        using (var writer = database.CreateContext())
        {
            var edited = writer.Sessions.Single();
            edited.Earnings = 3m;
            SessionService.Update(edited.Id, edited, database.Options);
        }
        Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(stale));
        using var read = database.CreateContext();
        Assert.Equal(3m, Assert.Single(read.Sessions).Earnings);
        Assert.Equal(2, read.SessionChanges.Count());
        Assert.Empty(read.Payouts);
        Assert.Empty(read.PayoutChanges);
    }

    [Fact]
    public void RefreshDuringInlineDraftRequiresReloadInsteadOfSavingStaleStatus()
    {
        var item = new SessionItemViewModel(Recording());
        item.BeginEdit();
        var refreshed = Recording(3m);
        refreshed.IsForceVerified = true;
        refreshed.PostedDate = new(2026, 6, 7);
        item.RefreshSession(refreshed);
        Assert.False(item.TryCreateEditedSession(out _, out var error));
        Assert.Contains("changed", error, StringComparison.OrdinalIgnoreCase);
        Assert.True(item.IsEditing);
        Assert.True(item.Session.IsForceVerified);
        item.CancelEdit();
        item.BeginEdit();
        Assert.True(item.TryCreateEditedSession(out var edited, out error), error);
        Assert.True(edited.IsForceVerified);
        Assert.Equal(3m, edited.Earnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleManualEditorCannotOverwriteNewerValuesOrTheirAudit(bool payout)
    {
        using var database = new FileDatabase();
        if (payout)
        {
            var original = PayoutService.Create(Payment().Payouts.Single(), options: database.Options);
            var state = PayoutService.CaptureEditState(original);
            using var writer = database.CreateContext();
            var newer = writer.Payouts.Single();
            newer.Amount = -15m;
            PayoutService.Update(newer.Id, newer, options: database.Options, expectedState: state);
            original.Description = "Stale description edit";
            Assert.Throws<InvalidOperationException>(() => PayoutService.Update(original.Id, original,
                options: database.Options, expectedState: state));
            using var read = database.CreateContext();
            Assert.Equal(-15m, read.Payouts.Single().Amount);
            Assert.Equal("Synthetic payment", read.Payouts.Single().Description);
            var audits = read.PayoutChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, audits.Count);
            using var before = JsonDocument.Parse(audits[1].BeforeValuesJson);
            Assert.Equal(-10m, before.RootElement.GetProperty("Amount").GetDecimal());
        }
        else
        {
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording()]));
            using var initial = database.CreateContext();
            var original = initial.Sessions.Single();
            var state = SessionService.CaptureEditState(original);
            SessionService.Update(original.Id, Recording(3m), database.Options, state);
            original.Duration = TimeSpan.FromMinutes(2);
            Assert.Throws<InvalidOperationException>(() => SessionService.Update(original.Id, original, database.Options, state));
            using var read = database.CreateContext();
            Assert.Equal(3m, read.Sessions.Single().Earnings);
            Assert.Equal(TimeSpan.FromMinutes(1), read.Sessions.Single().Duration);
            var audits = read.SessionChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, audits.Count);
            using var before = JsonDocument.Parse(audits[1].BeforeValuesJson);
            Assert.Equal(1m, before.RootElement.GetProperty("Earnings").GetDecimal());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelCommitsOfTheSamePreviewIdentitySaveExactlyOnce(bool payout)
    {
        using var database = new FileDatabase();
        using var ready = new Barrier(2);
        Task<bool> Commit() => Task.Run(() =>
        {
            using var payouts = new PayoutImporter(database.CreateContext());
            var sessions = new SessionImporter(database.Options);
            var payoutPreview = payout ? payouts.PreviewImport(Payment()) : null;
            var sessionPreview = payout ? null : sessions.PreviewImport([Recording()]);
            if (!ready.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Other writer did not preview.");
            try
            {
                if (payout) payouts.ImportPayouts(payoutPreview!);
                else sessions.ImportSessions(sessionPreview!);
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("preview", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        });
        var results = await Task.WhenAll(Commit(), Commit());
        Assert.Single(results, saved => saved);
        using var read = database.CreateContext();
        Assert.Equal(payout ? 1 : 0, read.Payouts.Count());
        Assert.Equal(payout ? 1 : 0, read.PayoutChanges.Count());
        Assert.Equal(payout ? 0 : 1, read.Sessions.Count());
        Assert.Equal(payout ? 0 : 1, read.SessionChanges.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedFinancialWriteDoesNotInvalidatePreview(bool payout)
    {
        using var database = new FileDatabase();
        var sessions = new SessionImporter(database.Options);
        using var payouts = new PayoutImporter(database.CreateContext());
        var sessionPreview = sessions.PreviewImport([Recording()]);
        var payoutPreview = payouts.PreviewImport(Payment());
        using (var writer = database.CreateContext())
        {
            var unrelated = Recording();
            unrelated.GameName = "Unrelated";
            writer.Sessions.Add(unrelated);
            writer.SaveChanges();
        }
        if (payout) payouts.ImportPayouts(payoutPreview);
        else sessions.ImportSessions(sessionPreview);
        using var read = database.CreateContext();
        Assert.Equal(payout ? 1 : 2, read.Sessions.Count());
        Assert.Equal(payout ? 1 : 0, read.Payouts.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingWriterLockFailsWithoutPartialWritesAndAllowsRetry(bool payout)
    {
        using var database = new FileDatabase();
        var sessions = new SessionImporter(database.Options);
        using var payouts = new PayoutImporter(database.CreateContext());
        var sessionPreview = sessions.PreviewImport([Recording()]);
        var payoutPreview = payouts.PreviewImport(Payment());
        void Commit()
        {
            if (payout) payouts.ImportPayouts(payoutPreview);
            else sessions.ImportSessions(sessionPreview);
        }
        using (var blocker = database.CreateContext())
        using (var transaction = blocker.Database.BeginTransaction())
        {
            var error = Assert.Throws<SqliteException>(Commit);
            Assert.Equal(5, error.SqliteErrorCode);
        }
        using (var read = database.CreateContext())
        {
            Assert.Empty(read.Sessions); Assert.Empty(read.Payouts);
            Assert.Empty(read.SessionChanges); Assert.Empty(read.PayoutChanges);
        }
        Commit();
        using var saved = database.CreateContext();
        Assert.Equal(payout ? 1 : 0, saved.PayoutChanges.Count());
        Assert.Equal(payout ? 0 : 1, saved.SessionChanges.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedTargetRejectsMixedImportWithoutPartialWrites(bool payout)
    {
        using var database = new FileDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording()]));
        using var payouts = new PayoutImporter(database.CreateContext());
        var parsed = Payment();
        parsed.ForceVerifiedSessions.Add(Recording(2m));
        var payoutPreview = payouts.PreviewImport(parsed);
        var extra = Recording(); extra.Date = extra.RecordedDate = extra.Date.AddDays(1);
        var sessionPreview = sessions.PreviewImport([extra, Recording(2m)]);
        using (var writer = database.CreateContext())
            SessionService.Delete(writer.Sessions.Single().Id, database.Options);
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (payout) payouts.ImportPayouts(payoutPreview);
            else sessions.ImportSessions(sessionPreview);
        });
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions); Assert.Empty(read.Payouts); Assert.Empty(read.PayoutChanges);
        Assert.Equal(2, read.SessionChanges.Count()); // Initial import and audited deletion only.
    }

    [Theory]
    [InlineData("session")]
    [InlineData("payout")]
    [InlineData("force")]
    public void NewCompetingCandidateInvalidatesPreviouslyUniqueMatch(string kind)
    {
        using var database = new FileDatabase();
        var sessions = new SessionImporter(database.Options);
        sessions.ImportSessions(sessions.PreviewImport([Recording()]));
        using var payouts = new PayoutImporter(database.CreateContext());
        payouts.ImportPayouts(payouts.PreviewImport(Payment()));
        using var previewer = new PayoutImporter(database.CreateContext());
        var parsed = Payment(-12m);
        if (kind == "force") parsed.ForceVerifiedSessions.Add(Recording(2m));
        var payoutPreview = previewer.PreviewImport(parsed);
        var sessionPreview = sessions.PreviewImport([Recording(2m)]);
        // Synthetic legacy collision from a writer outside the application's mutex.
        using (var writer = database.CreateContext())
        {
            if (kind == "payout") writer.Payouts.Add(Payment().Payouts.Single());
            else writer.Sessions.Add(Recording());
            writer.SaveChanges();
        }
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (kind == "session") sessions.ImportSessions(sessionPreview);
            else previewer.ImportPayouts(payoutPreview);
        });
        using var read = database.CreateContext();
        Assert.All(read.Sessions, session => Assert.Equal(1m, session.Earnings));
        Assert.All(read.Payouts, payment => Assert.Equal(-10m, payment.Amount));
        Assert.Single(read.SessionChanges); Assert.Single(read.PayoutChanges);
    }

    private sealed class FileDatabase : IDisposable
    {
        internal DbContextOptions<AppDbContext> Options { get; }
        internal FileDatabase()
        {
            var directory = Path.Combine(TestStorageIsolation.DirectoryPath, "pro11-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
                $"Data Source={Path.Combine(directory, "synthetic.db")};Pooling=False;Foreign Keys=True;Default Timeout=1").Options;
            using var db = CreateContext();
            db.Database.Migrate();
        }
        internal AppDbContext CreateContext() => new(Options);
        public void Dispose() { } // Retain disposable synthetic files for investigation; no personal data is read.
    }
}
