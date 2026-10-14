using EarningsTracker.Enums;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities;

internal sealed record EarningsPeriods(decimal Today, decimal Week, decimal Month)
{
    // Shared reporting rules from Statistics; posted dates still belong to balance accounting.
    internal static EarningsPeriods Calculate(IEnumerable<Session> sessions, IEnumerable<Payout> payouts, DateTime asOfDate)
    {
        var today = asOfDate.Date;
        var startOfWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var startOfMonth = new DateTime(today.Year, today.Month, 1);
        var earnings = sessions.Where(session => session.Status == SessionStatus.Verified)
            .Select(session => (Day: (session.RecordedDate == default ? session.Date : session.RecordedDate).Date, Amount: session.Earnings))
            .Concat(payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment)
                .Select(payout => (Day: payout.Date.Date, Amount: payout.Amount)))
            .Where(row => row.Day <= today).ToList();
        return new(earnings.Where(row => row.Day == today).Sum(row => row.Amount),
            earnings.Where(row => row.Day >= startOfWeek).Sum(row => row.Amount),
            earnings.Where(row => row.Day >= startOfMonth).Sum(row => row.Amount));
    }
}
