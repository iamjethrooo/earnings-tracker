using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    // The application event is static. Keep subscribers isolated from other test collections.
    [CollectionDefinition("DataChanged", DisableParallelization = true)]
    public class DataChangedCollection { }

    [Collection("DataChanged")]
    public class DataChangedTests
    {
        [Fact]
        public void PaymentChangesNotifyWithCommittedTotals()
        {
            using var database = new TestDatabase();
            var paidTotals = new List<decimal>();
            EventHandler handler = (_, _) =>
            {
                using var read = database.CreateContext();
                paidTotals.Add(PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalPaidOut);
            };
            PayoutService.DataChanged += handler;
            try
            {
                var payment = new Payout { Date = new DateTime(2026, 6, 5), Type = "Payment",
                    Description = "Test payout", Status = "Processing", Amount = 10m };
                PayoutService.Create(payment, options: database.Options);
                payment.Status = "Failed";
                PayoutService.Update(payment.Id, payment, options: database.Options);
                PayoutService.SoftDeleteManual(payment.Id, options: database.Options);
                Assert.Equal(new[] { 10m, 0m, 0m }, paidTotals);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void SessionImportEditAndDeleteNotifyWithCommittedEarnings()
        {
            using var database = new TestDatabase();
            var earnedTotals = new List<decimal>();
            EventHandler handler = (_, _) =>
            {
                using var read = database.CreateContext();
                earnedTotals.Add(PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
            };
            PayoutService.DataChanged += handler;
            try
            {
                var date = new DateTime(2026, 6, 5);
                var session = new Session { GameName = "Example", Date = date, RecordedDate = date,
                    Duration = TimeSpan.FromMinutes(10), Status = SessionStatus.Verified, Earnings = 1m };
                var importer = new SessionImporter(database.Options);
                importer.ImportSessions(importer.PreviewImport([session]));
                session.Earnings = 2m;
                SessionService.Update(session.Id, session, database.Options);
                SessionService.Delete(session.Id, database.Options);
                Assert.Equal(new[] { 1m, 2m, 0m }, earnedTotals);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void FailedImportDoesNotPublishADataChange()
        {
            using var database = new TestDatabase();
            using (var setup = database.CreateContext())
                setup.Database.ExecuteSqlRaw("CREATE TRIGGER reject_payout_audit BEFORE INSERT ON PayoutChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
            var notifications = 0;
            EventHandler handler = (_, _) => notifications++;
            PayoutService.DataChanged += handler;
            try
            {
                using var importer = new PayoutImporter(database.CreateContext());
                var preview = importer.PreviewImport(PayoutTextParser.ParseDetailed("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|"));
                Assert.Throws<DbUpdateException>(() => importer.ImportPayouts(preview));
                Assert.Equal(0, notifications);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void PayoutImportNotifiesAfterItsAuditIsCommitted()
        {
            using var database = new TestDatabase();
            var observedCounts = new List<(int Payouts, int Changes)>();
            EventHandler handler = (_, _) =>
            {
                using var read = database.CreateContext();
                observedCounts.Add((read.Payouts.Count(), read.PayoutChanges.Count()));
            };
            PayoutService.DataChanged += handler;
            try
            {
                using var importer = new PayoutImporter(database.CreateContext());
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
                Assert.Equal((1, 1), Assert.Single(observedCounts));
            }
            finally { PayoutService.DataChanged -= handler; }
        }
    }
}
