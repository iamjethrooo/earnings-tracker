using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.Tests;

public class ImportEvidenceTests
{
    private const string Recording = "Example\nvideo.mkv · 6/5/2026 12:00 PM · 10m\n  +$1.00  \nVerified";

    private static List<Session> Parse(TestDatabase database, string text)
    {
        using var db = database.CreateContext();
        return SessionTextParser.Parse(text, db.Games.ToList(), null);
    }

    private static void AddGame(TestDatabase database)
    {
        using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" });
        db.SaveChanges();
    }

    private static string? Source(SessionChange change) => change.SourceText;

    [Fact]
    public void RecordingImportKeepsFilenameDatesSignedAmountAndOriginalSpacing()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using var db = database.CreateContext();
        Assert.Equal(Recording, Source(Assert.Single(db.SessionChanges)));
    }

    [Fact]
    public void GameDetailsImportsKeepEachRecordingBlockSeparately()
    {
        using var database = new TestDatabase();
        AddGame(database);
        using var db = database.CreateContext();
        var game = Assert.Single(db.Games);
        const string first = "Nerve_20260605120000123.mkv\n6/5/2026 12:00 PM · 10m\n+$1.00\nVerified";
        const string second = "6/6/2026 12:00 PM · 20m\n+$2.00\nVerified";
        var importer = new SessionImporter(database.Options);
        var sessions = SessionTextParser.Parse(first + "\n\n" + second, [game], game);
        importer.ImportSessions(importer.PreviewImport(sessions));
        using var read = database.CreateContext();
        Assert.Equal(new[] { first, second }, read.SessionChanges.OrderBy(c => c.Id).AsEnumerable().Select(Source));
    }

    [Fact]
    public void UpdatedImportKeepsBothSourcesAndRepeatedImportAddsNoHistory()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        var updated = Recording.Replace("+$1.00", "+$2.00");
        importer.ImportSessions(importer.PreviewImport(Parse(database, updated)));
        importer.ImportSessions(importer.PreviewImport(Parse(database, updated)));
        using var db = database.CreateContext();
        Assert.Equal(new[] { Recording, updated }, db.SessionChanges.OrderBy(c => c.Id).AsEnumerable().Select(Source));
        Assert.Equal(2m, Assert.Single(db.Sessions).Earnings);
    }

    [Fact]
    public void EditsAndDeletionCannotEraseImportedEvidence()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using (var db = database.CreateContext())
        {
            var session = Assert.Single(db.Sessions);
            session.Earnings = 3m;
            SessionService.Update(session.Id, session, database.Options);
            SessionService.Delete(session.Id, database.Options);
        }
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        Assert.Equal(Recording, Source(read.SessionChanges.OrderBy(c => c.Id).First()));
        Assert.All(read.SessionChanges.OrderBy(c => c.Id).Skip(1), c => Assert.Null(Source(c)));
    }

    [Fact]
    public void ReimportEnrichesMissingEvidenceOnceWithoutChangingEarnings()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        var session = Parse(database, Recording).Single();
        // Simulate a historical import whose normalized values survive but whose source was discarded.
        using (var db = database.CreateContext())
        {
            session.PostedDate = session.Date;
            db.Sessions.Add(session);
            db.SaveChanges();
        }
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using var read = database.CreateContext();
        var evidence = Assert.Single(read.SessionChanges);
        Assert.Equal("Imported evidence", evidence.ChangeType);
        Assert.Equal(evidence.BeforeValuesJson, evidence.AfterValuesJson);
        Assert.Equal(Recording, Source(evidence));
        Assert.Equal(1m, Assert.Single(read.Sessions).Earnings);
    }

    [Fact]
    public void FailedAuditInsertRollsBackImportedRecordingAndEvidence()
    {
        using var database = new TestDatabase();
        AddGame(database);
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_source_audit BEFORE INSERT ON SessionChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        var importer = new SessionImporter(database.Options);
        Assert.Throws<DbUpdateException>(() => importer.ImportSessions(importer.PreviewImport(Parse(database, Recording))));
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        Assert.Empty(read.SessionChanges);
    }

    [Fact]
    public void RepeatedBaselineMigrationKeepsHistoryAndDoesNotInventSourceEvidence()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Database.ExecuteSqlRaw("INSERT INTO SessionChanges (SessionId, ChangedAt, ChangeType, BeforeValuesJson, AfterValuesJson) VALUES (42, '2026-06-05', 'Deleted', {0}, {1});", "{\"Earnings\":1}", "{}");
        db.Database.Migrate();
        var history = Assert.Single(db.SessionChanges);
        Assert.Equal(42, history.SessionId);
        Assert.Equal("{\"Earnings\":1}", history.BeforeValuesJson);
        Assert.Null(Source(history));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PayoutForceVerificationKeepsTheTransactionRowInSessionHistory()
    {
        using var database = new TestDatabase();
        var date = new DateTime(2026, 6, 5, 12, 0, 0);
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(1), Status = SessionStatus.Pending });
            db.SaveChanges();
        }
        const string row = "|6/6/2026|6/5/2026 12:00|Earning|Example — 1 min recording (force-verified)|Verified|+$1.50|";
        using (var importer = new PayoutImporter(database.CreateContext()))
            importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(row)));
        using var read = database.CreateContext();
        Assert.Equal(row, Assert.Single(read.SessionChanges).SourceText);
        Assert.True(Assert.Single(read.Sessions).IsForceVerified);
        Assert.Empty(read.Payouts);
    }

    [Fact]
    public void FailedUpdatedImportRetainsPreviousValuesAndSource()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_update_audit BEFORE INSERT ON SessionChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        var updated = Recording.Replace("+$1.00", "+$2.00");
        Assert.Throws<DbUpdateException>(() => importer.ImportSessions(importer.PreviewImport(Parse(database, updated))));
        using var read = database.CreateContext();
        Assert.Equal(1m, Assert.Single(read.Sessions).Earnings);
        Assert.Equal(Recording, Assert.Single(read.SessionChanges).SourceText);
    }

    [Fact]
    public void ReimportAfterDeletionPreservesOldHistoryAndRestoresTheRecordingAsBefore()
    {
        using var database = new TestDatabase();
        AddGame(database);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using (var db = database.CreateContext())
            SessionService.Delete(Assert.Single(db.Sessions).Id, database.Options);
        importer.ImportSessions(importer.PreviewImport(Parse(database, Recording)));
        using var read = database.CreateContext();
        Assert.Single(read.Sessions);
        var changes = read.SessionChanges.OrderBy(change => change.Id).ToList();
        Assert.Equal(new[] { "Imported", "Deleted", "Imported" }, changes.Select(change => change.ChangeType));
        Assert.Equal(Recording, changes[0].SourceText);
        Assert.Equal(Recording, changes[2].SourceText);
    }

    [Fact]
    public void UnchangedForceVerificationEnrichesHistoricalEvidenceOnlyOnce()
    {
        using var database = new TestDatabase();
        var date = new DateTime(2026, 6, 5, 12, 0, 0);
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", Date = date, RecordedDate = date,
                PostedDate = new DateTime(2026, 6, 6), IsForceVerified = true,
                Duration = TimeSpan.FromMinutes(1), Status = SessionStatus.Verified, Earnings = 1.5m });
            db.SaveChanges();
        }
        const string row = "|6/6/2026|6/5/2026 12:00|Earning|Example — 1 min recording (force-verified)|Verified|+$1.50|";
        for (var i = 0; i < 2; i++)
        {
            using var importer = new PayoutImporter(database.CreateContext());
            importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(row)));
        }
        using var read = database.CreateContext();
        var audit = Assert.Single(read.SessionChanges);
        Assert.Equal("Imported evidence", audit.ChangeType);
        Assert.Equal(audit.BeforeValuesJson, audit.AfterValuesJson);
        Assert.Equal(row, audit.SourceText);
    }
}
