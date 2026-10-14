using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class AuditHistoryTests
    {
        [Fact]
        public void ManualAdjustmentReconciliationChangesTotalsAndKeepsBeforeAfterHistory()
        {
            using var database = new TestDatabase();
            var adjustment = new Payout { Date = new DateTime(2026, 6, 5), Type = "Bonus", Description = "Bug reports",
                Status = "Verified", Amount = 20m, ReconciliationState = "Separate" };
            PayoutService.Create(adjustment, "Manual bonus", database.Options);
            using (var read = database.CreateContext())
                Assert.Equal(20m, PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalEarned);
            adjustment.IsAdjustmentIncludedInSessionEarnings = true;
            adjustment.ReconciliationState = "IncludedInSessions";
            PayoutService.Update(adjustment.Id, adjustment, "Already in sessions", database.Options);
            using var verify = database.CreateContext();
            Assert.Equal(0m, PayoutAccounting.Calculate([], verify.Payouts.ToList()).TotalEarned);
            var changes = verify.PayoutChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, changes.Count);
            using var before = JsonDocument.Parse(changes[1].BeforeValuesJson);
            using var after = JsonDocument.Parse(changes[1].AfterValuesJson);
            Assert.Equal("Separate", before.RootElement.GetProperty("ReconciliationState").GetString());
            Assert.Equal("IncludedInSessions", after.RootElement.GetProperty("ReconciliationState").GetString());
            Assert.Equal("Already in sessions", changes[1].Reason);
        }

        [Fact]
        public void ImportedAdjustmentRequiresReconciliationAndCannotBeDeleted()
        {
            using var database = new TestDatabase();
            const string row = "|6/5/2026|—|Bonus|Bug reports|Verified|+$20.00|";
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(row)));
            Payout adjustment;
            using (var read = database.CreateContext())
            {
                adjustment = Assert.Single(read.Payouts);
                Assert.Equal("NeedsReview", adjustment.ReconciliationState);
                Assert.Equal(0m, PayoutAccounting.Calculate([], [adjustment]).TotalEarned);
            }
            Assert.Throws<InvalidOperationException>(() => PayoutService.SoftDeleteManual(adjustment.Id, options: database.Options));
            adjustment.ReconciliationState = "Separate";
            adjustment.Amount = 19m;
            PayoutService.Update(adjustment.Id, adjustment, "Corrected amount", database.Options);
            using var verify = database.CreateContext();
            var saved = Assert.Single(verify.Payouts);
            Assert.Equal(row, saved.SourceRow);
            Assert.Equal(19m, PayoutAccounting.Calculate([], [saved]).TotalEarned);
            var changes = verify.PayoutChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, changes.Count);
            using var original = JsonDocument.Parse(changes[0].AfterValuesJson);
            Assert.Equal(20m, original.RootElement.GetProperty("Amount").GetDecimal());
            Assert.Equal(row, original.RootElement.GetProperty("SourceRow").GetString());
        }

        [Fact]
        public void ReimportUpdatePreservesOriginalSourceInAuditHistory()
        {
            using var database = new TestDatabase();
            const string first = "|6/5/2026|—|Payment|Batch payout|Processing|$10.00|";
            const string second = "|6/5/2026|—|Payment|Batch payout|Paid|-$12.00|";
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(first)));
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(second)));
            using var read = database.CreateContext();
            Assert.Equal(second, Assert.Single(read.Payouts).SourceRow);
            var changes = read.PayoutChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, changes.Count);
            using var before = JsonDocument.Parse(changes[1].BeforeValuesJson);
            using var after = JsonDocument.Parse(changes[1].AfterValuesJson);
            Assert.Equal(first, before.RootElement.GetProperty("SourceRow").GetString());
            Assert.Equal(10m, before.RootElement.GetProperty("Amount").GetDecimal());
            Assert.Equal(-12m, after.RootElement.GetProperty("Amount").GetDecimal());
        }

        [Fact]
        public void DeletingManualPaymentRetainsAuditAndExcludesItFromTotals()
        {
            using var database = new TestDatabase();
            var payment = new Payout { Date = new DateTime(2026, 6, 5), Type = "Payment", Description = "Manual payment", Status = "Paid", Amount = -10m };
            PayoutService.Create(payment, options: database.Options);
            PayoutService.SoftDeleteManual(payment.Id, "Mistaken entry", database.Options);
            using var read = database.CreateContext();
            Assert.Empty(read.Payouts);
            Assert.True(Assert.Single(read.Payouts.IgnoreQueryFilters()).IsDeleted);
            Assert.Equal(2, read.PayoutChanges.Count());
            Assert.Equal(0m, PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalPaidOut);
        }

        [Fact]
        public void SessionEditsAndDeletionRetainHistoricalValues()
        {
            using var database = new TestDatabase();
            var date = new DateTime(2026, 6, 5, 12, 0, 0);
            var session = new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(10), Earnings = 1m, Status = SessionStatus.Verified };
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([session]));
            session.Earnings = 2m;
            SessionService.Update(session.Id, session, database.Options);
            SessionService.Delete(session.Id, database.Options);
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            var changes = read.SessionChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(3, changes.Count);
            Assert.Equal("Deleted", changes[2].ChangeType);
            using var initial = JsonDocument.Parse(changes[0].AfterValuesJson);
            using var deleted = JsonDocument.Parse(changes[2].BeforeValuesJson);
            Assert.Equal(1m, initial.RootElement.GetProperty("Earnings").GetDecimal());
            Assert.Equal(2m, deleted.RootElement.GetProperty("Earnings").GetDecimal());
        }

        [Fact]
        public void DatabaseFailureRollsBackPayoutAndItsAuditEntry()
        {
            using var database = new TestDatabase();
            using (var setup = database.CreateContext())
                setup.Database.ExecuteSqlRaw("CREATE TRIGGER reject_payout_audit BEFORE INSERT ON PayoutChanges BEGIN SELECT RAISE(ABORT, 'Test audit failure'); END;");
            using var importer = new PayoutImporter(database.CreateContext());
            var preview = importer.PreviewImport(PayoutTextParser.ParseDetailed("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|"));
            Assert.Throws<DbUpdateException>(() => importer.ImportPayouts(preview));
            using var read = database.CreateContext();
            Assert.Empty(read.Payouts);
            Assert.Empty(read.PayoutChanges);
        }

        [Fact]
        public void InvalidManualAdjustmentDoesNotWriteDataOrAudit()
        {
            using var database = new TestDatabase();
            var adjustment = new Payout { Date = new DateTime(2026, 6, 5), Type = "Bonus", Description = "Bug reports", Status = "Verified", Amount = 20m };
            Assert.Throws<ArgumentException>(() => PayoutService.Create(adjustment, options: database.Options));
            using var read = database.CreateContext();
            Assert.Empty(read.Payouts);
            Assert.Empty(read.PayoutChanges);
        }

        [Theory]
        [InlineData(false, "Separate", 20)]
        [InlineData(true, "IncludedInSessions", 0)]
        public void ReimportPreservesAdjustmentReconciliation(bool included, string state, int expectedEarned)
        {
            using var database = new TestDatabase();
            const string row = "|6/5/2026|—|Bonus|Bug reports|Verified|+$20.00|";
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(row)));
            Payout adjustment;
            using (var read = database.CreateContext())
                adjustment = Assert.Single(read.Payouts);
            adjustment.IsAdjustmentIncludedInSessionEarnings = included;
            adjustment.ReconciliationState = state;
            PayoutService.Update(adjustment.Id, adjustment, "Reconciled", database.Options);
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(row)));
            using var verify = database.CreateContext();
            var saved = Assert.Single(verify.Payouts);
            Assert.Equal(state, saved.ReconciliationState);
            Assert.Equal(included, saved.IsAdjustmentIncludedInSessionEarnings);
            Assert.Equal(expectedEarned, PayoutAccounting.Calculate([], [saved]).TotalEarned);
            Assert.Equal(2, verify.PayoutChanges.Count());
        }
    }
}
