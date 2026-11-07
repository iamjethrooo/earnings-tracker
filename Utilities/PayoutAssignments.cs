using System.ComponentModel;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Utilities;

public sealed record SessionPayoutOption(bool IsExplicit, int? PayoutId, string Label);

public sealed class PayoutSessionChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    internal SessionService.EditState ExpectedState { get; }
    public int SessionId { get; }
    public decimal Earnings { get; }
    public string Label { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal PayoutSessionChoice(Session session)
    {
        SessionId = session.Id;
        Earnings = session.Earnings;
        ExpectedState = SessionService.CaptureEditState(session);
        Label = $"#{session.Id} · {session.GameName} · recorded {session.RecordedDate:MMM d, yyyy h:mm tt} · {CurrencyDisplay.FormatUsd(session.Earnings)}";
        _isSelected = true;
    }
}

public sealed class PayoutCoveragePreview : INotifyPropertyChanged
{
    internal Payout Payment { get; }
    internal (DateTime Date, string Type, string Status, decimal Amount) PaymentState { get; }
    internal IReadOnlySet<int> ExcludedSessionIds { get; }
    public IReadOnlyList<PayoutSessionChoice> Sessions { get; }
    public string Heading => $"Sessions covered · {Payment.Date:MMM d, yyyy} · {CurrencyDisplay.FormatUsd(Math.Abs(Payment.Amount))}";
    public string Summary
    {
        get
        {
            var selected = Sessions.Where(s => s.IsSelected).ToList();
            var difference = Math.Abs(Payment.Amount) - selected.Sum(s => s.Earnings);
            return $"{selected.Count} selected · {CurrencyDisplay.FormatUsd(selected.Sum(s => s.Earnings))} · "
                + (difference == 0 ? "Matches payment." : $"Difference: {CurrencyDisplay.FormatUsd(difference)}. Adjustments or fees may explain it.")
                + " Unchecked sessions remain in Current payout.";
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal PayoutCoveragePreview(Payout payment, IEnumerable<Session> sessions, IReadOnlySet<int>? excludedSessionIds = null)
    {
        Payment = payment;
        PaymentState = (payment.Date, payment.Type, payment.Status, payment.Amount);
        ExcludedSessionIds = excludedSessionIds ?? new HashSet<int>();
        Sessions = sessions.Select(s => new PayoutSessionChoice(s)).ToList();
        foreach (var choice in Sessions)
            choice.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new(nameof(Summary)));
    }
}

internal static class PayoutAssignments
{
    internal static List<SessionPayoutOption> Options(IEnumerable<Payout> payments) =>
        [new(false, null, "Historical · inferred from posted date"), new(true, null, "Current payout · awaiting payment"),
         .. payments.Where(p => !p.IsDeleted && PayoutAccounting.IsCountedAsPaidPayment(p))
             .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
             .Select(p => new SessionPayoutOption(true, p.Id, PaymentLabel(p)))];

    internal static string PaymentLabel(Payout payment) =>
        $"{payment.Date:MMM d, yyyy} · {CurrencyDisplay.FormatUsd(Math.Abs(payment.Amount))} · {payment.Status} · {payment.SourceId ?? $"#{payment.Id}"}";

    internal static void ValidateAssignment(AppDbContext db, Session session, Session? previous = null)
    {
        if (!session.HasExplicitPayoutAssignment && session.AssignedPayoutId.HasValue)
            throw new ArgumentException("Choose Current payout or a payment for this session.");
        if (session.AssignedPayoutId is not int id) return;
        // Retain existing membership when a payment is hidden or changes status.
        if (previous?.AssignedPayoutId == id) return;
        var payment = db.Payouts.AsNoTracking().SingleOrDefault(p => p.Id == id);
        if (payment == null || !PayoutAccounting.IsCountedAsPaidPayment(payment))
            throw new ArgumentException("The selected payment is no longer Paid or Processing. Reload sessions and choose again.");
    }

    internal static bool NeedsCoverage(Payout payment, Payout? previous = null) =>
        PayoutAccounting.IsCountedAsPaidPayment(payment) &&
        (previous == null || !PayoutAccounting.IsCountedAsPaidPayment(previous));

    internal static PayoutCoveragePreview Preview(AppDbContext db, Payout payment, IReadOnlySet<int>? excludedSessionIds = null)
    {
        var payments = db.Payouts.AsNoTracking().ToList();
        var sessions = db.Sessions.AsNoTracking().Where(s => s.Status == SessionStatus.Verified).ToList();
        var candidates = PayoutAccounting.CurrentPayoutSessions(sessions, payments)
            .Where(s => s.PostedDate!.Value.Date <= payment.Date.Date && excludedSessionIds?.Contains(s.Id) != true)
            .OrderBy(s => s.RecordedDate).ThenBy(s => s.Id);
        return new(payment, candidates, excludedSessionIds);
    }

    internal static void ValidateCoverage(AppDbContext db, Payout payment, PayoutCoveragePreview preview)
    {
        if (preview.PaymentState != (payment.Date, payment.Type, payment.Status, payment.Amount)
            || !PayoutAccounting.IsCountedAsPaidPayment(payment))
            throw new InvalidOperationException("The payment changed. Review its session coverage again before saving.");
        var current = Preview(db, payment, preview.ExcludedSessionIds);
        if (!current.Sessions.Select(s => (s.SessionId, s.ExpectedState))
            .SequenceEqual(preview.Sessions.Select(s => (s.SessionId, s.ExpectedState))))
            throw new InvalidOperationException("Sessions or payments changed after the coverage preview. Preview again; no changes were committed.");
    }

    internal static void ApplyCoverage(AppDbContext db, Payout payment, PayoutCoveragePreview preview,
        ISet<int>? assignedInBatch = null)
    {
        foreach (var choice in preview.Sessions)
        {
            if (assignedInBatch?.Contains(choice.SessionId) == true) continue;
            var session = db.Sessions.Single(s => s.Id == choice.SessionId);
            var before = SessionService.Snapshot(session);
            int? assignedId = choice.IsSelected ? payment.Id : null;
            if (session.HasExplicitPayoutAssignment && session.AssignedPayoutId == assignedId) continue;
            session.HasExplicitPayoutAssignment = true;
            session.AssignedPayoutId = assignedId;
            if (choice.IsSelected) assignedInBatch?.Add(session.Id);
            db.SessionChanges.Add(new SessionChange
            {
                SessionId = session.Id, ChangedAt = DateTime.Now, ChangeType = "Payout assignment",
                BeforeValuesJson = before, AfterValuesJson = SessionService.Snapshot(session)
            });
        }
    }
}
