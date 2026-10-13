using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Exceptions;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.Tests
{
    [Collection("DataChanged")]
    public class PostCommitRefreshTests
    {
        private static Payout Payment() => new()
        {
            Date = new DateTime(2026, 6, 5), Type = "Payment", Description = "Test payment",
            Status = "Processing", Amount = 10m
        };

        private static Session Recording() => new()
        {
            GameName = "Example", Date = new DateTime(2026, 6, 5), RecordedDate = new DateTime(2026, 6, 5),
            Status = SessionStatus.Verified, Duration = TimeSpan.FromMinutes(10), Earnings = 1m
        };

        [Theory]
        [Trait("Category", "Regression")]
        [InlineData("Create payout")]
        [InlineData("Update payout")]
        [InlineData("Hide payout")]
        [InlineData("Import payouts")]
        [InlineData("Import sessions")]
        [InlineData("Update session")]
        [InlineData("Delete session")]
        public void RefreshFailureIsReportedAsCommittedAndDoesNotStopOtherSubscribers(string operation)
        {
            using var database = new TestDatabase();
            var payout = Payment();
            var session = Recording();
            // Seed before subscribing so only the operation under test triggers the failure.
            if (operation is "Update payout" or "Hide payout")
                PayoutService.Create(payout, options: database.Options);
            if (operation is "Update session" or "Delete session")
            {
                var importer = new SessionImporter(database.Options);
                importer.ImportSessions(importer.PreviewImport([session]));
            }
            var refreshFailure = new InvalidOperationException("Test refresh failure");
            EventHandler failingHandler = (_, _) => throw refreshFailure;
            var laterNotifications = 0;
            EventHandler laterHandler = (_, _) => laterNotifications++;
            PayoutService.DataChanged += failingHandler;
            PayoutService.DataChanged += laterHandler;
            try
            {
                var error = Assert.Throws<PostCommitRefreshException>(() =>
                {
                    switch (operation)
                    {
                        case "Create payout":
                            PayoutService.Create(payout, options: database.Options);
                            break;
                        case "Update payout":
                            payout.Amount = 12m;
                            PayoutService.Update(payout.Id, payout, options: database.Options);
                            break;
                        case "Hide payout":
                            PayoutService.SoftDeleteManual(payout.Id, options: database.Options);
                            break;
                        case "Import payouts":
                            using (var importer = new PayoutImporter(database.CreateContext()))
                                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(
                                    "|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
                            break;
                        case "Import sessions":
                            var sessionImporter = new SessionImporter(database.Options);
                            sessionImporter.ImportSessions(sessionImporter.PreviewImport([session]));
                            break;
                        case "Update session":
                            session.Earnings = 2m;
                            SessionService.Update(session.Id, session, database.Options);
                            break;
                        case "Delete session":
                            SessionService.Delete(session.Id, database.Options);
                            break;
                        default:
                            throw new ArgumentException("Unknown test operation.");
                    }
                });
                Assert.Contains("saved", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, laterNotifications);
                Assert.Same(refreshFailure, Assert.Single(Assert.IsType<AggregateException>(error.InnerException).InnerExceptions));

                using var read = database.CreateContext();
                switch (operation)
                {
                    case "Create payout":
                    case "Import payouts":
                        Assert.Single(read.Payouts);
                        Assert.Single(read.PayoutChanges);
                        Assert.Equal(10m, PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalPaidOut);
                        break;
                    case "Update payout":
                        Assert.Equal(12m, Assert.Single(read.Payouts).Amount);
                        Assert.Equal(2, read.PayoutChanges.Count());
                        break;
                    case "Hide payout":
                        Assert.Empty(read.Payouts);
                        Assert.True(Assert.Single(read.Payouts.IgnoreQueryFilters()).IsDeleted);
                        Assert.Equal(2, read.PayoutChanges.Count());
                        break;
                    case "Import sessions":
                        Assert.Single(read.Sessions);
                        Assert.Single(read.SessionChanges);
                        break;
                    case "Update session":
                        Assert.Equal(2m, Assert.Single(read.Sessions).Earnings);
                        Assert.Equal(2, read.SessionChanges.Count());
                        break;
                    case "Delete session":
                        Assert.Empty(read.Sessions);
                        Assert.Equal(2, read.SessionChanges.Count());
                        Assert.Equal("Deleted", read.SessionChanges.OrderBy(change => change.Id).Last().ChangeType);
                        break;
                }
            }
            finally
            {
                PayoutService.DataChanged -= failingHandler;
                PayoutService.DataChanged -= laterHandler;
            }
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MultipleRefreshFailuresAreReportedTogetherAfterEverySubscriberRuns()
        {
            using var database = new TestDatabase();
            EventHandler first = (_, _) => throw new InvalidOperationException("First refresh failure");
            EventHandler second = (_, _) => throw new InvalidOperationException("Second refresh failure");
            var lastNotifications = 0;
            EventHandler last = (_, _) => lastNotifications++;
            PayoutService.DataChanged += first;
            PayoutService.DataChanged += second;
            PayoutService.DataChanged += last;
            try
            {
                var error = Assert.Throws<PostCommitRefreshException>(() => PayoutService.Create(Payment(), options: database.Options));
                Assert.Equal(2, Assert.IsType<AggregateException>(error.InnerException).InnerExceptions.Count);
                Assert.Equal(1, lastNotifications);
                using var read = database.CreateContext();
                Assert.Single(read.Payouts);
                Assert.Single(read.PayoutChanges);
            }
            finally
            {
                PayoutService.DataChanged -= first;
                PayoutService.DataChanged -= second;
                PayoutService.DataChanged -= last;
            }
        }

        [Fact]
        public void DatabaseFailureStillRollsBackAndDoesNotNotifyOrReportACompletedSave()
        {
            using var database = new TestDatabase();
            using (var setup = database.CreateContext())
                setup.Database.ExecuteSqlRaw("CREATE TRIGGER reject_payout_audit BEFORE INSERT ON PayoutChanges BEGIN SELECT RAISE(ABORT, 'Test database failure'); END;");
            var notifications = 0;
            EventHandler handler = (_, _) => { notifications++; throw new InvalidOperationException("Must not be called"); };
            PayoutService.DataChanged += handler;
            try
            {
                Assert.Throws<DbUpdateException>(() => PayoutService.Create(Payment(), options: database.Options));
                Assert.Equal(0, notifications);
                using var read = database.CreateContext();
                Assert.Empty(read.Payouts);
                Assert.Empty(read.PayoutChanges);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void ReimportAfterRefreshWarningRecognizesThePreviouslyCommittedRows()
        {
            using var database = new TestDatabase();
            const string row = "|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|";
            EventHandler failingHandler = (_, _) => throw new InvalidOperationException("Test refresh failure");
            PayoutService.DataChanged += failingHandler;
            try
            {
                using var importer = new PayoutImporter(database.CreateContext());
                Assert.Throws<PostCommitRefreshException>(() => importer.ImportPayouts(
                    importer.PreviewImport(PayoutTextParser.ParseDetailed(row))));
            }
            finally { PayoutService.DataChanged -= failingHandler; }

            using (var importer = new PayoutImporter(database.CreateContext()))
            {
                var preview = importer.PreviewImport(PayoutTextParser.ParseDetailed(row));
                Assert.Equal(1, preview.UnchangedCount);
                Assert.Equal(0, preview.AddedCount);
                Assert.Equal(0, preview.UpdatedCount);
                importer.ImportPayouts(preview);
            }
            using var read = database.CreateContext();
            Assert.Single(read.Payouts);
            Assert.Single(read.PayoutChanges);
        }
    }
}
