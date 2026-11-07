using EarningsTracker.Enums;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities
{
    internal sealed record PayoutTotals(decimal TotalEarned, decimal TotalPaidOut, decimal Balance, int UndatedVerifiedSessions);

    internal static class PayoutAccounting
    {
        public static PayoutTotals Calculate(IEnumerable<Session> sessions, IEnumerable<Payout> payouts)
        {
            var verifiedSessions = sessions.Where(session => session.Status == SessionStatus.Verified).ToList();
            var payoutRows = payouts.ToList();

            var earnedAdjustments = payoutRows
                .Where(IsSeparateEarnedAdjustment)
                .Sum(payout => payout.Amount);
            var totalEarned = verifiedSessions.Sum(session => session.Earnings) + earnedAdjustments;

            var paidPayments = payoutRows.Where(IsCountedAsPaidPayment).ToList();
            var totalPaidOut = paidPayments.Sum(payout => Math.Abs(payout.Amount));
            var undatedVerifiedSessions = verifiedSessions.Count(session => session.PostedDate == null);
            var balance = CurrentPayoutSessions(verifiedSessions, payoutRows)
                .Sum(session => session.Earnings);

            return new PayoutTotals(totalEarned, totalPaidOut, balance, undatedVerifiedSessions);
        }

        internal static IEnumerable<Session> CurrentPayoutSessions(IEnumerable<Session> sessions, IEnumerable<Payout> payouts)
        {
            var paidPayments = payouts.Where(p => !p.IsDeleted && IsCountedAsPaidPayment(p)).ToList();
            var paidIds = paidPayments.Select(p => p.Id).ToHashSet();
            var latestDate = paidPayments.Count == 0 ? (DateTime?)null : paidPayments.Max(p => p.Date.Date);
            return sessions.Where(session => session.PostedDate.HasValue &&
                (session.HasExplicitPayoutAssignment
                    ? !session.AssignedPayoutId.HasValue || !paidIds.Contains(session.AssignedPayoutId.Value)
                    : !latestDate.HasValue || session.PostedDate.Value.Date > latestDate.Value));
        }

        internal static bool IsSeparateEarnedAdjustment(Payout payout)
        {
            var isAdjustment = payout.Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase)
                || payout.Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase);
            var hasEarnedStatus = payout.Status.Equals("Verified", StringComparison.OrdinalIgnoreCase)
                || payout.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase)
                || payout.Status.Equals("Processing", StringComparison.OrdinalIgnoreCase);
            return isAdjustment && !payout.IsAdjustmentIncludedInSessionEarnings
                && payout.ReconciliationState == "Separate" && hasEarnedStatus;
        }

        internal static bool IsCountedAsPaidPayment(Payout payout)
        {
            var isPayment = payout.Type.Equals("Payment", StringComparison.OrdinalIgnoreCase);
            var isPaidStatus = payout.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase)
                || payout.Status.Equals("Processing", StringComparison.OrdinalIgnoreCase);
            return isPayment && isPaidStatus;
        }
    }
}
