using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.Tests
{
    public class SessionImporterTests
    {
        private static Session Recording(int seconds = 1, decimal earnings = 1m)
        {
            var date = new DateTime(2026, 6, 5, 12, 0, seconds);
            return new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(1), Earnings = earnings, Status = SessionStatus.Verified };
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ReimportDoesNotDuplicateSessionsOrAuditUnchangedValues()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording()]));
            importer.ImportSessions(importer.PreviewImport([Recording()]));
            using var read = database.CreateContext();
            var session = Assert.Single(read.Sessions);
            Assert.Equal(session.RecordedDate, session.PostedDate);
            Assert.Single(read.SessionChanges);
            Assert.Equal(1m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ConflictingVersionsOfANewRecordingAreNotInserted()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording(1, 1m), Recording(1, 2m)]);
            Assert.Empty(preview.NewSessions);
            Assert.Equal(2, preview.AmbiguousCount);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void DistinctPreciseTimestampsWithinAMinuteSurviveImportAndReimport()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording(1), Recording(20)]);
            Assert.Equal(2, preview.InsertedCount);
            Assert.Equal(0, preview.SkippedCount);
            importer.ImportSessions(preview);
            importer.ImportSessions(importer.PreviewImport([Recording(1), Recording(20)]));
            using var read = database.CreateContext();
            Assert.Equal(2, read.Sessions.Count());
            Assert.Equal(2, read.SessionChanges.Count());
            Assert.Equal(2m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MinuteOnlyMatchDoesNotOverwriteDifferentPreciseTimestamp()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(1)]));
            var preview = importer.PreviewImport([Recording(20, 2m)]);
            Assert.Empty(preview.Updates);
            Assert.Single(preview.AmbiguousSessions);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            var session = Assert.Single(read.Sessions);
            Assert.Equal(Recording(1).RecordedDate, session.RecordedDate);
            Assert.Equal(1m, session.Earnings);
        }

        [Fact]
        public void MultipleIndistinguishableDatabaseMatchesAreSkipped()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Sessions.AddRange(Recording(), Recording());
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording(1, 2m)]);
            Assert.Single(preview.AmbiguousSessions);
            Assert.Empty(preview.Updates);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.All(read.Sessions, session => Assert.Equal(1m, session.Earnings));
            Assert.Empty(read.SessionChanges);
        }

        [Fact]
        public void IdenticalRowsWithinOneImportAreSkipped()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording(), Recording()]);
            Assert.Equal(1, preview.InsertedCount);
            Assert.Equal(1, preview.SkippedCount);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Single(read.Sessions);
            Assert.Single(read.SessionChanges);
        }

        [Fact]
        public void VerifiedReimportPreservesKnownForceVerificationPostedDate()
        {
            using var database = new TestDatabase();
            var session = Recording();
            session.IsForceVerified = true;
            session.PostedDate = new DateTime(2026, 6, 10);
            using (var seed = database.CreateContext())
            {
                seed.Sessions.Add(session);
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(1, 2m)]));
            using var read = database.CreateContext();
            var updated = Assert.Single(read.Sessions);
            Assert.True(updated.IsForceVerified);
            Assert.Equal(new DateTime(2026, 6, 10), updated.PostedDate);
            Assert.Equal(2m, updated.Earnings);
            Assert.Single(read.SessionChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MissingForceVerificationDateIsNotInvented()
        {
            using var database = new TestDatabase();
            var session = Recording();
            session.IsForceVerified = true;
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([session]));
            using var read = database.CreateContext();
            Assert.Null(Assert.Single(read.Sessions).PostedDate);
            Assert.Equal(1, PayoutAccounting.Calculate(read.Sessions.ToList(), []).UndatedVerifiedSessions);
        }

        [Fact]
        public void FailureDuringSecondSaveRollsBackSessionsAndAudit()
        {
            using var database = new TestDatabase();
            using (var setup = database.CreateContext())
                setup.Database.ExecuteSqlRaw("CREATE TRIGGER reject_session_audit BEFORE INSERT ON SessionChanges BEGIN SELECT RAISE(ABORT, 'Test audit failure'); END;");
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording()]);
            Assert.Throws<DbUpdateException>(() => importer.ImportSessions(preview));
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
        }
    }
}
