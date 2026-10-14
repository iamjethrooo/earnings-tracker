using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    [Collection("DataChanged")]
    public class SessionEarningsValidationTests
    {
        private static Session Recording(int day, decimal earnings)
        {
            var date = new DateTime(2026, 6, day, 12, 0, 1);
            return new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(1), Earnings = earnings, Status = SessionStatus.Verified };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Category", "Regression")]
        public void SessionPreviewRejectsNegativeEarningsForNewAndExistingRecords(bool update)
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            if (update)
                importer.ImportSessions(importer.PreviewImport([Recording(5, 1m)]));

            var error = Assert.Throws<ArgumentException>(() => importer.PreviewImport([Recording(5, -1m)]));
            Assert.Contains("earnings adjustment", error.Message);
            using var read = database.CreateContext();
            Assert.Equal(update ? 1 : 0, read.Sessions.Count());
            Assert.Equal(update ? 1 : 0, read.SessionChanges.Count());
            Assert.All(read.Sessions, session => Assert.Equal(1m, session.Earnings));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Category", "Regression")]
        public void InvalidatedSessionPreviewCannotSaveAnyPartOfTheBatch(bool invalidUpdate)
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(5, 1m)]));
            var preview = importer.PreviewImport([Recording(5, 2m), Recording(6, 3m)]);
            if (invalidUpdate)
                Assert.Single(preview.Updates).Imported.Earnings = -1m;
            else
                Assert.Single(preview.NewSessions).Earnings = -1m;

            var notifications = 0;
            EventHandler handler = (_, _) => notifications++;
            PayoutService.DataChanged += handler;
            try
            {
                Assert.Throws<ArgumentException>(() => importer.ImportSessions(preview));
                Assert.Equal(0, notifications);
            }
            finally { PayoutService.DataChanged -= handler; }

            using var read = database.CreateContext();
            Assert.Equal(1m, Assert.Single(read.Sessions).Earnings);
            Assert.Single(read.SessionChanges);
            Assert.Equal(1m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void PayoutPreviewRejectsNegativeForceVerifiedSessionEarnings()
        {
            using var database = new TestDatabase();
            using var importer = new PayoutImporter(database.CreateContext());
            var parsed = new PayoutParseResult();
            parsed.ForceVerifiedSessions.Add(Recording(5, -1m));
            Assert.Throws<ArgumentException>(() => importer.PreviewImport(parsed));
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void InvalidatedForceVerificationCannotSavePaymentsOrSessionHistory()
        {
            using var database = new TestDatabase();
            var sessions = new SessionImporter(database.Options);
            sessions.ImportSessions(sessions.PreviewImport([Recording(5, 1m)]));
            using var importer = new PayoutImporter(database.CreateContext());
            var parsed = PayoutTextParser.ParseDetailed("""
                |6/10/2026|—|Payment|Batch payout|Paid|-$10.00|
                |6/10/2026|6/5/2026 12:00:01|Earning|Example — 1 min recording (force-verified)|Verified|+$2.00|
                """);
            var preview = importer.PreviewImport(parsed);
            Assert.Single(preview.NewPayouts);
            Assert.Single(preview.ForceVerifiedSessionUpdates).Session.Earnings = -1m;

            var notifications = 0;
            EventHandler handler = (_, _) => notifications++;
            PayoutService.DataChanged += handler;
            try
            {
                Assert.Throws<ArgumentException>(() => importer.ImportPayouts(preview));
                Assert.Equal(0, notifications);
            }
            finally { PayoutService.DataChanged -= handler; }

            using var read = database.CreateContext();
            var session = Assert.Single(read.Sessions);
            Assert.Equal(1m, session.Earnings);
            Assert.False(session.IsForceVerified);
            Assert.Equal(session.RecordedDate, session.PostedDate);
            Assert.Single(read.SessionChanges);
            Assert.Empty(read.Payouts);
            Assert.Empty(read.PayoutChanges);
        }

        [Fact]
        public void SessionImportPreservesZeroAndExistingDecimalPrecision()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(5, 0m), Recording(6, 1.2345m)]));
            using var read = database.CreateContext();
            Assert.Equal(0m, read.Sessions.Single(session => session.Date.Day == 5).Earnings);
            Assert.Equal(1.2345m, read.Sessions.Single(session => session.Date.Day == 6).Earnings);
            Assert.Equal(2, read.SessionChanges.Count());
            Assert.Equal(1.2345m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
        }
    }
}
