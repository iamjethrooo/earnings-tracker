using EarningsTracker.Models;
using EarningsTracker.Enums;
using EarningsTracker.Utilities;
using System.Windows.Media;

namespace EarningsTracker.ViewModels;

public sealed record DashboardDay(DateTime Date, decimal Verified, decimal Adjustments, decimal Pending)
{
    public decimal Earned => Verified + Adjustments;
    public string Description => $"{Date:MMM d, yyyy} · Verified {CurrencyDisplay.FormatUsd(Verified)} · Adjustments {CurrencyDisplay.FormatUsd(Adjustments)} · Earned {CurrencyDisplay.FormatUsd(Earned)} · Pending {CurrencyDisplay.FormatUsd(Pending)}";
}

public sealed record DashboardMetric(string Label, string Value, string Detail, Brush ValueBrush);

public sealed record DashboardStatus(SessionStatus Status, int Count, decimal Amount, Brush Color)
{
    public string Label => Status == SessionStatus.NotUploaded ? "Not Uploaded" : Status.ToString();
    public string Description => $"{Label} · {Count:N0}";
    public string AmountText => CurrencyDisplay.FormatUsd(Amount);
}

public sealed record DashboardGame(Game? Game, string Name, int Sessions, decimal Earnings, double Share, SessionItemViewModel Artwork)
{
    public bool CanNavigate => Game != null;
    public string Amount => CurrencyDisplay.FormatUsd(Earnings);
    public string Detail => $"{Sessions:N0} verified sessions{(Game == null ? " · unlinked history" : Game.IsArchived ? " · archived" : "")}";
}
