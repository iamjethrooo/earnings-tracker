using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.Tests
{
    public class PayoutAccountingTests
    {
        [Fact]
        public void EmptyLedgerHasZeroTotals()
        {
            var totals = PayoutAccounting.Calculate([], []);
            Assert.Equal(0m, totals.TotalEarned);
            Assert.Equal(0m, totals.TotalPaidOut);
            Assert.Equal(0m, totals.Balance);
        }

        [Theory]
        [InlineData(SessionStatus.Verified, 10)]
        [InlineData(SessionStatus.Pending, 0)]
        [InlineData(SessionStatus.Rejected, 0)]
        [InlineData(SessionStatus.NotUploaded, 0)]
        public void OnlyVerifiedSessionsContributeToEarnings(SessionStatus status, int expected)
        {
            var session = new Session { Earnings = 10m, Status = status };
            Assert.Equal(expected, PayoutAccounting.Calculate([session], []).TotalEarned);
        }

        [Theory]
        [InlineData("Paid", 10)]
        [InlineData("Processing", 10)]
        [InlineData("Failed", 0)]
        [InlineData("Pending", 0)]
        [InlineData("Verified", 0)]
        [InlineData("Legacy", 0)]
        public void PaymentStatusesHaveExplicitAccounting(string status, int expected)
        {
            var payment = new Payout { Type = "Payment", Status = status, Amount = -10m };
            var totals = PayoutAccounting.Calculate([], [payment]);
            Assert.Equal(expected, totals.TotalPaidOut);
            Assert.Equal(0m, totals.TotalEarned);
            Assert.Equal(-10m, payment.Amount);
        }

        [Theory]
        [InlineData("Bonus", "Verified")]
        [InlineData("Bonus", "Paid")]
        [InlineData("Bonus", "Processing")]
        [InlineData("Earnings correction", "Verified")]
        public void ReconciledSeparateAdjustmentsContributeSignedEarnings(string type, string status)
        {
            var adjustment = new Payout { Type = type, Status = status, Amount = -2m, ReconciliationState = "Separate" };
            var totals = PayoutAccounting.Calculate([new Session { Earnings = 10m, Status = SessionStatus.Verified }], [adjustment]);
            Assert.Equal(8m, totals.TotalEarned);
            Assert.Equal(0m, totals.TotalPaidOut);
            Assert.Equal(-2m, adjustment.Amount);
        }

        [Theory]
        [InlineData("NeedsReview", false, "Verified")]
        [InlineData("IncludedInSessions", true, "Verified")]
        [InlineData("Separate", true, "Verified")]
        [InlineData("Separate", false, "Failed")]
        [InlineData("Separate", false, "Pending")]
        public void UnreconciledIncludedOrUnearnedAdjustmentsAreExcluded(string state, bool included, string status)
        {
            var adjustment = new Payout { Type = "Bonus", Status = status, Amount = 20m,
                ReconciliationState = state, IsAdjustmentIncludedInSessionEarnings = included };
            Assert.Equal(0m, PayoutAccounting.Calculate([], [adjustment]).TotalEarned);
        }

        [Fact]
        public void DecimalCentsAreAddedExactly()
        {
            var sessions = Enumerable.Range(0, 100).Select(_ => new Session { Status = SessionStatus.Verified, Earnings = 0.01m });
            Assert.Equal(1m, PayoutAccounting.Calculate(sessions, []).TotalEarned);
        }

        [Fact]
        public void BalanceUsesPostedDateAndIgnoresFailedPayments()
        {
            var payoutDate = new DateTime(2026, 6, 10);
            var sessions = new[]
            {
                new Session { Status = SessionStatus.Verified, Earnings = 2m, PostedDate = payoutDate.AddDays(-1) },
                new Session { Status = SessionStatus.Verified, Earnings = 3m, PostedDate = payoutDate },
                new Session { Status = SessionStatus.Verified, Earnings = 4m, Date = payoutDate.AddDays(-5), PostedDate = payoutDate.AddDays(1) },
                new Session { Status = SessionStatus.Verified, Earnings = 5m },
                new Session { Status = SessionStatus.Pending, Earnings = 6m, PostedDate = payoutDate.AddDays(1) }
            };
            var payments = new[]
            {
                new Payout { Type = "Payment", Status = "Processing", Amount = 5m, Date = payoutDate },
                new Payout { Type = "Payment", Status = "Failed", Amount = 100m, Date = payoutDate.AddDays(2) }
            };
            var totals = PayoutAccounting.Calculate(sessions, payments);
            Assert.Equal(14m, totals.TotalEarned);
            Assert.Equal(5m, totals.TotalPaidOut);
            Assert.Equal(4m, totals.Balance);
            Assert.Equal(1, totals.UndatedVerifiedSessions);
        }

        [Fact]
        public void BalanceBeforeFirstPaymentIncludesAllDatedVerifiedSessions()
        {
            var sessions = new[] { new Session { Status = SessionStatus.Verified, Earnings = 3m, PostedDate = new DateTime(2026, 6, 5) } };
            Assert.Equal(3m, PayoutAccounting.Calculate(sessions, []).Balance);
        }
    }
}
