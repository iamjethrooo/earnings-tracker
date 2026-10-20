using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace EarningsTracker.Prototypes.Games;

public sealed record SampleOffer(string Store, string Edition, decimal? Amount, string Currency,
    string Region, DateTime Checked, bool IsStale = false, bool Available = true)
{
    public bool IsCurrent => Available && Region == "PH" && !IsStale;
    public bool IsFree => IsCurrent && Amount == 0;
    public string Price => Amount is decimal amount ? Currency switch
    {
        "PHP" => "₱" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-US")),
        "USD" => "US$" + amount.ToString("N2", CultureInfo.GetCultureInfo("en-US")),
        _ => $"{Currency} {amount.ToString("N2", CultureInfo.GetCultureInfo("en-US"))}"
    } : "Price unavailable";
    public string Context => $"{Store} · {Edition} · PC / {Region}";
    public string Evidence => $"Checked {Checked:MMM d, yyyy}" +
        (IsStale ? " · stale, last known price" : !Available || Region != "PH" ? " · unavailable in PH" : Amount == null ? " · amount unknown" : " · sample offer");
    public Brush EvidenceBrush => (Brush)Application.Current.FindResource(IsStale || !Available || Region != "PH" ? "PendingTextColor" : "SecondaryTextColor");
}

public sealed record SampleLibraryGame(int Id, string Name, string Genre, decimal Rate, decimal CapHours,
    decimal UsedHours, decimal CycleEarnings, decimal CycleMaximum, DateTime Added, bool IsArchived,
    ImageSource? Cover, IReadOnlyList<SampleOffer> Offers, string Subscription, DateTime? SubscriptionChecked,
    string SteamSlot, string InstalledSlot, decimal? PurchaseCost, string PurchaseCurrency)
{
    public bool SubscriptionCurrent => SubscriptionChecked.HasValue && SubscriptionChecked.Value >= DashboardViewModel.AsOf.Date.AddDays(-7);
}

public sealed class GameLibraryItem : INotifyPropertyChanged
{
    private bool _isBookmarked, _isOwned;
    public SampleLibraryGame Game { get; }
    public ImageSource? Cover => Game.Cover;
    public SampleOffer PrimaryOffer => Game.Offers[0];
    public IReadOnlyList<SampleOffer> Offers => Game.Offers;
    public string Name => Game.Name;
    public bool IsArchived => Game.IsArchived;
    public decimal Rate => Game.Rate;
    public DateTime Added => Game.Added;
    public bool IsBookmarked { get => _isBookmarked; set { _isBookmarked = value; Notify(); } }
    public bool IsOwned { get => _isOwned; set { _isOwned = value; Notify(); } }
    public bool IsPlayable => IsOwned || PrimaryOffer.IsFree || Game.SubscriptionCurrent;
    public bool HasStaleInformation => PrimaryOffer.IsStale || Game.SubscriptionChecked.HasValue && !Game.SubscriptionCurrent;
    public bool PriceUnknown => PrimaryOffer.Amount == null || !PrimaryOffer.Available || PrimaryOffer.Region != "PH";
    public string OwnershipAction => IsOwned ? "Owned · remove" : "Mark owned";
    public string OwnershipHint => IsOwned ? "Remove the personal owned flag; sample purchase cost stays intact." : "Mark personally owned in memory. Steam demo indicators are independent.";
    public string BookmarkAction => IsBookmarked ? $"Remove bookmark for {Name}" : $"Bookmark {Name}";
    public string BookmarkGlyph => IsBookmarked ? "★" : "☆";
    public string Metadata => $"{Game.Genre} · PC";
    public string RateText => DashboardViewModel.Usd(Game.Rate);
    public string RateBasis => IsArchived ? "Historical rate" : "Sample rate";
    public string CapText => $"{Game.UsedHours:0.##} / {Game.CapHours:0.##} h";
    public double CapPercent => Game.CapHours == 0 ? 0 : (double)(Game.UsedHours / Game.CapHours * 100m);
    public string CapDetail => $"{CapPercent:0}% · since Oct 1";
    public string PotentialText => IsArchived ? "History only" : DashboardViewModel.Usd(Game.CycleMaximum);
    public string PotentialBasis => IsArchived ? "No active earnings" : "USD · full cycle";
    public string PriceText => !PrimaryOffer.Available || PrimaryOffer.Region != "PH" ? "Unavailable in PH"
        : PrimaryOffer.IsFree ? "Free-to-play" : PrimaryOffer.Price;
    public string PriceContext => $"{PrimaryOffer.Store} · {PrimaryOffer.Edition}";
    public string PriceEvidence => PrimaryOffer.IsStale ? "Stale price · excluded from price sort"
        : !PrimaryOffer.Available || PrimaryOffer.Region != "PH" ? "Unavailable in PH · excluded from price sort"
        : PrimaryOffer.Amount == null ? "Unknown price · excluded from price sort"
        : PrimaryOffer.Currency is not ("PHP" or "USD") ? "No sample FX · excluded from price sort"
        : $"PH · checked {PrimaryOffer.Checked:MMM d}";
    public Brush PriceBrush => (Brush)Application.Current.FindResource(HasStaleInformation || PriceUnknown ? "PendingTextColor" : "SecondaryTextColor");
    public string AccessText => IsOwned ? "Owned" : PrimaryOffer.IsFree ? "Free access" : Game.SubscriptionCurrent ? "PC Game Pass · included"
        : Game.SubscriptionChecked.HasValue ? "PC Game Pass · stale" : "Purchase / access needed";
    public Brush AccessBrush => (Brush)Application.Current.FindResource(IsPlayable ? "VerifiedTextColor" : HasStaleInformation ? "PendingTextColor" : "SecondaryTextColor");
    public string SubscriptionText => !Game.SubscriptionChecked.HasValue ? "No verified subscription listing in this fixture."
        : $"{Game.Subscription} · PC / PH\nChecked {Game.SubscriptionChecked:MMM d, yyyy}\n" +
          (Game.SubscriptionCurrent ? "Included with the active sample plan. Subscription fees are separate." : "Stale listing. Not counted as playable through this subscription.");
    public string FxText => !PrimaryOffer.IsCurrent || PrimaryOffer.Amount == null
        ? "Excluded from Lowest price: stale, unknown or unavailable PH offer. Source amounts are retained; no current price comparison."
        : PrimaryOffer.Currency switch
    {
        "USD" => $"≈ ₱{PrimaryOffer.Amount.Value * 56.25m:N2} for Lowest price · sample FX 56.25 PHP/USD, Oct 19. Estimate only; source price remains USD.",
        "PHP" => "PHP price used directly for Lowest price. Earnings are USD; comparing purchase cost with earnings requires currency conversion.",
        _ => "Exchange rate unavailable. Excluded from Lowest price; no converted price or cross-currency comparison."
    };
    // Only the base/primary offer is compared; stale or non-PH amounts are not advertised as current offers.
    public decimal PriceSortValue => PrimaryOffer is { IsCurrent: true, Amount: decimal amount } ? PrimaryOffer.Currency switch
    {
        "PHP" => amount, "USD" => amount * 56.25m, _ => decimal.MaxValue
    } : decimal.MaxValue;
    public string PurchaseText => Game.PurchaseCost.HasValue
        ? $"Saved sample cost: {Game.PurchaseCurrency} {Game.PurchaseCost:0.00}. Kept when personal ownership is toggled."
        : "Purchase cost unknown. Marking owned does not invent a cost.";
    public string Identity => $"SAMPLE-GAME-{Game.Id:0000} · added {Game.Added:MMM d, yyyy}";
    public string EarningsSummary => $"{DashboardViewModel.Usd(Game.CycleEarnings)} verified in this sample cycle · recording date since Oct 1, 2026.";
    public string PotentialExplanation => "Illustrative ceiling at the displayed rate and cap, not a forecast or balance. Stored historical earnings and rates are not recalculated.";
    public string ArchiveExplanation => IsArchived ? "Archived game · historical rate and cap retained. Existing sessions and rate/reset history remain readable." : "Active sample game · current-cycle values are synthetic.";
    public Brush ArchiveBrush => (Brush)Application.Current.FindResource(IsArchived ? "ArchivedTextColor" : "SecondaryTextColor");
    public event PropertyChangedEventHandler? PropertyChanged;

    public GameLibraryItem(SampleLibraryGame game, bool owned, bool bookmarked)
    { Game = game; _isOwned = owned; _isBookmarked = bookmarked; }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
