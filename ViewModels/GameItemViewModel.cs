using EarningsTracker.Commands;
using EarningsTracker.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EarningsTracker.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using EarningsTracker.Utilities;
using System.Windows.Media;

namespace EarningsTracker.ViewModels
{
    public class GameItemViewModel : INotifyPropertyChanged
    {
        public Game Game { get; }

        public GameMetadata? Metadata { get; private set; }
        public List<string> Genres { get; private set; } = [];
        public List<StoreOffer> Offers { get; private set; } = [];
        public List<SubscriptionAvailability> Subscriptions { get; private set; } = [];
        public GameOwnership? Ownership { get; private set; }
        public IReadOnlyDictionary<string, ExchangeRate> Rates { get; private set; } = new Dictionary<string, ExchangeRate>();
        private ImageSource? _cover;
        private bool _coverRequested;
        public ImageSource? Cover
        {
            get { if (!_coverRequested) { _coverRequested = true; _ = LoadCoverAsync(); } return _cover; }
        }
        private async Task LoadCoverAsync()
        {
            var metadata = Metadata;
            if (metadata == null) return;
            var cover = await CatalogArtwork.LoadAsync(metadata.CoverUrl, metadata.CoverSha256);
            if (Metadata?.CoverSha256 != metadata.CoverSha256 || Metadata?.CoverUrl != metadata.CoverUrl) return;
            _cover = cover; OnPropertyChanged(nameof(Cover));
        }
        internal SteamMatch SteamMatch { get; private set; }
        private DateTime? _steamSyncedAt;
        private bool _steamStale;
        public bool IsSteamOwned => SteamMatch == SteamMatch.Matched;
        public string SteamBadge => _steamStale ? "Steam library · saved" : "Steam library";
        public bool ShowsSteamInstallation => _steamInstalled.HasValue;
        public string SteamInstallation => _steamInstalled == true
            ? "Steam installation detected on this PC."
            : "Steam installation on this PC is unknown.";
        private bool? _steamInstalled;
        public string SteamEvidence => SteamMatch switch
        {
            SteamMatch.Matched => $"Seen in Steam library · synced {_steamSyncedAt?.ToLocalTime():g}"
                + (_steamStale ? " · may be out of date" : ""),
            SteamMatch.NotObserved => "Steam library did not report the reviewed AppID. Access is unknown.",
            SteamMatch.Unmapped => "No reviewed Steam AppID match for this game.",
            SteamMatch.Ambiguous => "Steam AppID evidence conflicts. Access is unknown.",
            _ => "Steam library evidence unavailable."
        };
        public bool IsOwned => Ownership?.IsOwned == true;
        public string OwnershipButtonText => IsOwned ? "Owned · remove" : "Mark owned";
        public string OwnershipAccessibleName => $"Toggle manual ownership for {Game.Name}";
        public string OwnershipTooltip => IsOwned ? "Remove from owned games. Saved purchase cost is kept."
            : "Add to owned games. Edit purchase cost in game details.";
        public string OwnershipError { get; private set; } = "";
        public ICommand ToggleOwnedCommand { get; }
        private PersonalGameRatings _ratings;
        private PersonalGameRatings _suggestions;
        private int _grindingSources, _replaySources;
        internal PersonalGameRatings Ratings => _ratings;
        public int GrindingRating => _ratings.Grinding;
        public int ReplayRating => _ratings.Replay;
        public int GrindingRank => GrindingRating != 0 ? GrindingRating : _suggestions.Grinding;
        public int ReplayRank => ReplayRating != 0 ? ReplayRating : _suggestions.Replay;
        public string GrindingRatingText => (GrindingRating == 0 && GrindingRank != 0 ? "Suggested grinding ease · " : "My grinding ease · ") + (GrindingRank switch
        {
            3 => "Easy", 2 => "Moderate", 1 => "Hard", _ => "Unrated"
        });
        public string ReplayRatingText => (ReplayRating == 0 && ReplayRank != 0 ? "Suggested replay appeal · " : "My replay appeal · ") + (ReplayRank switch
        {
            3 => "High", 2 => "Medium", 1 => "Low", _ => "Unrated"
        });
        public string GrindingRatingHelp => RatingHelp(GrindingRating, _grindingSources);
        public string ReplayRatingHelp => RatingHelp(ReplayRating, _replaySources);

        private static string RatingHelp(int rating, int sources) => rating != 0 ? "Your rating for this game."
            : sources == 0 ? "No personal rating or suggestion."
            : $"Based on {sources} {(sources == 1 ? "game" : "games")} you rated with similar reviewed genres.";

        internal void SetRatingSuggestions(PersonalGameRatings ratings, int grindingSources, int replaySources)
        {
            if (_suggestions == ratings && _grindingSources == grindingSources && _replaySources == replaySources) return;
            _suggestions = ratings;
            _grindingSources = grindingSources;
            _replaySources = replaySources;
            NotifyRatingRanks();
        }

        internal void SetRatings(PersonalGameRatings ratings)
        {
            if (_ratings == ratings) return;
            _ratings = ratings;
            OnPropertyChanged(nameof(GrindingRating));
            OnPropertyChanged(nameof(ReplayRating));
            NotifyRatingRanks();
            OnPropertyChanged(nameof(Ratings));
        }

        private void NotifyRatingRanks()
        {
            OnPropertyChanged(nameof(GrindingRank));
            OnPropertyChanged(nameof(ReplayRank));
            OnPropertyChanged(nameof(GrindingRatingText));
            OnPropertyChanged(nameof(ReplayRatingText));
            OnPropertyChanged(nameof(GrindingRatingHelp));
            OnPropertyChanged(nameof(ReplayRatingHelp));
        }

        internal void SaveRatings(int grinding, int replay)
        {
            var ratings = new PersonalGameRatings(grinding, replay);
            new AppSettings(_databaseOptions).SaveGameRatings(Game.Id, ratings);
            SetRatings(ratings);
        }

        public string GenreText => Genres.Count == 0 ? "Genre unknown" : string.Join(" · ", Genres);
        public string SourceText => Offers.Count == 0 && Subscriptions.Count == 0 ? "Sources unknown"
            : string.Join(" · ", Offers.Select(x => x.Source).Concat(Subscriptions.Where(x => x.Included).Select(x => x.Service)).Distinct());
        public string Summary => string.IsNullOrWhiteSpace(Metadata?.Summary) ? "" : Metadata.Summary;
        public string MetadataStatus => Metadata?.Reviewed == true
            ? $"{Metadata.Provenance} · verified {Metadata.VerifiedAt:MMM d, yyyy}" : Game.IsArchived ? "Archived · metadata optional" : "Unresolved catalog entry";
        public bool FreeToPlay => Offers.Any(x => (x.Kind == AcquisitionKind.FreeToPlay || x.Kind == AcquisitionKind.Purchase)
            && CatalogPresentation.IsFree(x, DateTime.UtcNow));
        public decimal PriceSortValue => Offers.Any(x => CatalogPresentation.IsFree(x, DateTime.UtcNow)) ? 0m : LowestPrice ?? decimal.MaxValue;
        public decimal? LowestPrice => Offers.Select(x => CatalogPresentation.EffectivePrice(x, DateTime.UtcNow) is decimal price
            ? CatalogPresentation.ToPhp(price, x.Currency, Rates) : null).Where(x => x.HasValue).DefaultIfEmpty().Min();
        private StoreOffer? BestOffer => Offers.OrderBy(x => CatalogPresentation.IsFree(x, DateTime.UtcNow) ? 0 : 1)
            .ThenBy(x => CatalogPresentation.EffectivePrice(x, DateTime.UtcNow) is decimal price
                ? CatalogPresentation.ToPhp(price, x.Currency, Rates) ?? decimal.MaxValue : decimal.MaxValue)
            .ThenByDescending(x => x.VerifiedAt).FirstOrDefault(x => CatalogPresentation.IsFree(x, DateTime.UtcNow)
                || CatalogPresentation.EffectivePrice(x, DateTime.UtcNow).HasValue);
        public string PriceTooltip => BestOffer is StoreOffer offer ? CatalogPresentation.OfferText(offer, Rates, DateTime.UtcNow) : PriceText;
        public string AccessStatusText => Offers.Count == 0 ? PriceText : "";
        public string PriceText
        {
            get
            {
                if (FreeToPlay) return "Free-to-play";
                var now = DateTime.UtcNow;
                if (Offers.Any(x => x.Kind == AcquisitionKind.Giveaway && CatalogPresentation.IsFree(x, now))) return "Temporary giveaway";
                if (BestOffer is StoreOffer offer) return CatalogPresentation.CompactPrice(offer, now);
                return Subscriptions.Any(x => CatalogPresentation.Current(x, now)) ? "Subscription access · see details" : "Price unknown";
            }
        }
        public string BreakEvenText => CatalogPresentation.BreakEven(Game, Ownership, Rates);
        public List<OfferDisplay> OfferDetails => Offers.GroupBy(x => (x.Source.ToUpperInvariant(), x.Edition.ToUpperInvariant(), x.Region))
            .Select(group => group.OrderBy(x => CatalogPresentation.IsFree(x, DateTime.UtcNow) ? 0 : 1)
                .ThenBy(x => CatalogPresentation.EffectivePrice(x, DateTime.UtcNow) is decimal price ? CatalogPresentation.ToPhp(price, x.Currency, Rates) ?? decimal.MaxValue : decimal.MaxValue)
                .ThenByDescending(x => x.VerifiedAt).First())
            .Select(x => new OfferDisplay(x.Url, $"{x.Source}  ·  {CatalogPresentation.CompactPrice(x, DateTime.UtcNow)}",
                $"{x.Edition} · {x.Region} · checked {x.VerifiedAt:yyyy-MM-dd}"
                + ((x.SalePrice ?? x.RegularPrice) is decimal quote ? $" · saved quote {CatalogPresentation.Money(quote, x.Currency)}" : " · no recorded price")
                + (x.Region != "PH" ? " · not a PH offer" : "")
                + (x.ExpiresAt.HasValue ? $" · offer validity {x.ExpiresAt:yyyy-MM-dd HH:mm}" : ""), CatalogPresentation.OfferText(x, Rates, DateTime.UtcNow) + $"\nSource evidence: {x.Provenance}")
                { Heading = $"{x.Source} · {x.Edition} · PC / {x.Region}", Price = CatalogPresentation.CompactPrice(x, DateTime.UtcNow) }).ToList();
        public List<OfferDisplay> SubscriptionDetails => Subscriptions.Select(x => new OfferDisplay(x.Url,
            CatalogPresentation.SubscriptionText(x, DateTime.UtcNow), x.Provenance, IsWarning: !CatalogPresentation.VerificationCurrent(x, DateTime.UtcNow))
            { CanOpenLink = x.Service != "PC Game Pass" }).ToList();
        public string GamePassStatus => CatalogPresentation.GamePassEntry(Subscriptions) is { } entry
            ? CatalogPresentation.SubscriptionText(entry, DateTime.UtcNow) : "";
        public bool GamePassNeedsVerification => CatalogPresentation.GamePassEntry(Subscriptions) is { } entry
            && !CatalogPresentation.VerificationCurrent(entry, DateTime.UtcNow);
        public bool GamePassCurrentlyAvailable => CatalogPresentation.GamePassEntry(Subscriptions) is { } entry
            && CatalogPresentation.Current(entry, DateTime.UtcNow);
        public string PurchaseEvidence => $"{(IsOwned ? "Manually marked owned" : "Manual ownership unset")} · "
            + (Ownership?.PurchaseAmount is decimal amount ? $"saved cost {CatalogPresentation.Money(amount, Ownership.Currency)} · {Ownership.Source}" : "purchase cost not recorded");
        public string RateBasis => Game.IsArchived ? "Historical rate" : "";
        public string CycleCapText => $"{Game.MinutesCap / 60m:0.##} hours";
        private decimal _cycleMinutes;
        public bool HasCycleProgress => _cycleMinutes > 0;
        public decimal CycleProgressPercent => Game.MinutesCap > 0 ? _cycleMinutes / Game.MinutesCap * 100m : 0;
        public string CycleProgressText => $"{_cycleMinutes / 60m:0.##} / {Game.MinutesCap / 60m:0.##}h";
        public string CycleStatusText => !HasCycleProgress ? "Not started"
            : Game.MinutesCap > 0 && _cycleMinutes >= Game.MinutesCap ? "Cap reached" : "In progress";
        internal void SetCycleMinutes(decimal minutes)
        {
            _cycleMinutes = minutes;
            OnPropertyChanged(nameof(HasCycleProgress));
            OnPropertyChanged(nameof(CycleProgressPercent));
            OnPropertyChanged(nameof(CycleProgressText));
            OnPropertyChanged(nameof(CycleStatusText));
        }
        public string PotentialText => Game.IsArchived ? "History only" : CurrencyDisplay.FormatUsd(Game.RatePerHour * Game.MinutesCap / 60m);
        public string PotentialBasis => Game.IsArchived ? "Archived earning opportunity" : "";
        public string PriceEvidence => BestOffer is { } offer
            ? $"{offer.Source} · {offer.Edition} · {offer.Region} · checked {offer.VerifiedAt:yyyy-MM-dd}"
            : "No current comparable purchase offer. See acquisition details.";

        public string LibraryPriceEvidence
        {
            get
            {
                // Retain dated evidence even when there is no currently comparable price.
                var offer = BestOffer ?? Offers.OrderByDescending(x => x.VerifiedAt).FirstOrDefault();
                if (offer == null) return "No saved purchase offer";
                var now = DateTime.UtcNow;
                var checkedDate = offer.VerifiedAt == default ? "Check date unknown" : $"Checked {offer.VerifiedAt:MMM d, yyyy}";
                var status = offer.ExpiresAt <= now ? " · Expired offer"
                    : offer.ExpiresAt.HasValue ? $" · Expires {offer.ExpiresAt:MMM d, yyyy}" : "";
                return $"{offer.Source} · {checkedDate}{status}";
            }
        }

        internal void SetCatalog(GameMetadata? metadata, List<string> genres, List<StoreOffer> offers,
            List<SubscriptionAvailability> subscriptions, GameOwnership? ownership, IReadOnlyDictionary<string, ExchangeRate> rates,
            SteamMatch steamMatch = SteamMatch.Unknown, DateTime? steamSyncedAt = null, bool steamStale = false,
            bool? steamInstalled = null)
        {
            var artworkChanged = Metadata?.CoverSha256 != metadata?.CoverSha256 || Metadata?.CoverUrl != metadata?.CoverUrl;
            Metadata = metadata; Genres = genres;
            Offers = offers.Where(x => CatalogSources.PopularName(x.Source) != null).ToList();
            foreach (var offer in Offers) offer.Source = CatalogSources.PopularName(offer.Source)!;
            Subscriptions = subscriptions; Ownership = ownership; Rates = rates;
            SteamMatch = steamMatch; _steamSyncedAt = steamSyncedAt; _steamStale = steamStale;
            _steamInstalled = steamInstalled;
            if (artworkChanged) { _cover = null; _coverRequested = false; OnPropertyChanged(nameof(Cover)); }
            foreach (var property in new[] { nameof(Metadata), nameof(Genres), nameof(Offers), nameof(Subscriptions), nameof(Rates),
                nameof(GenreText), nameof(SourceText), nameof(Summary), nameof(MetadataStatus), nameof(FreeToPlay),
                nameof(PriceSortValue), nameof(LowestPrice), nameof(PriceText), nameof(PriceTooltip), nameof(AccessStatusText),
                nameof(OfferDetails), nameof(SubscriptionDetails), nameof(GamePassStatus), nameof(GamePassNeedsVerification), nameof(GamePassCurrentlyAvailable), nameof(PriceEvidence), nameof(LibraryPriceEvidence) }) OnPropertyChanged(property);
            NotifyOwnership();
            OnPropertyChanged(nameof(IsSteamOwned));
            OnPropertyChanged(nameof(SteamBadge));
            OnPropertyChanged(nameof(SteamEvidence));
            OnPropertyChanged(nameof(ShowsSteamInstallation));
            OnPropertyChanged(nameof(SteamInstallation));
        }

        private void NotifyOwnership()
        {
            foreach (var property in new[] { nameof(Ownership), nameof(IsOwned), nameof(OwnershipButtonText),
                nameof(OwnershipTooltip), nameof(OwnershipAccessibleName), nameof(BreakEvenText), nameof(PurchaseEvidence) }) OnPropertyChanged(property);
        }

        internal void SaveOwnership(bool owned, string source, decimal? amount, string currency)
        {
            if (amount < 0 || !CatalogService.ValidCurrency(currency)) throw new ArgumentException("Enter a nonnegative amount and a three-letter currency code.");
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
            var ownership = db.GameOwnership.Find(Game.Id);
            if (ownership == null) { ownership = new GameOwnership { GameId = Game.Id }; db.GameOwnership.Add(ownership); }
            ownership.IsOwned = owned; ownership.Source = source; ownership.PurchaseAmount = amount; ownership.Currency = currency;
            db.SaveChanges(); Ownership = ownership; NotifyOwnership();
        }

        private bool _isBookmarked;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private string _bookmarkError = "";
        public string BookmarkError
        {
            get => _bookmarkError;
            private set { _bookmarkError = value; OnPropertyChanged(); }
        }

        public ICommand ToggleBookmarkCommand { get; }
        public string BookmarkAccessibleName => IsBookmarked ? $"Remove bookmark for {Game.Name}" : $"Bookmark {Game.Name}";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand? OpenDetailsCommand { get; }


        public GameItemViewModel(Game game, bool isBookmarked) : this(game, isBookmarked, null) { }

        internal GameItemViewModel(Game game, bool isBookmarked, DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            Game = game;
            IsBookmarked = isBookmarked;
            ToggleBookmarkCommand = new RelayCommand(ToggleBookmark);
            ToggleOwnedCommand = new RelayCommand(_ =>
            {
                try
                {
                    SaveOwnership(!IsOwned, Ownership?.Source ?? "", Ownership?.PurchaseAmount, Ownership?.Currency ?? "PHP");
                    OwnershipError = "";
                }
                catch (Exception) { OwnershipError = "Ownership could not be saved. Please retry."; }
                OnPropertyChanged(nameof(OwnershipError));
            });
        }

        public bool IsBookmarked
        {
            get => _isBookmarked;
            set
            {
                if (_isBookmarked == value)
                    return;

                _isBookmarked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BookmarkAccessibleName));
            }
        }
        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName)
            );
        }

        private void ToggleBookmark(object? parameter)
        {
            bool isBookmarked;
            try
            {
                using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                var bookmark = db.Bookmarks.FirstOrDefault(b => b.GameId == Game.Id);
                isBookmarked = bookmark == null;
                if (isBookmarked)
                {
                    db.Bookmarks.Add(new Bookmark { GameId = Game.Id });
                }
                else
                {
                    db.Bookmarks.Remove(bookmark!);
                }
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Unable to save bookmark for game {Game.Id}: {ex}");
                BookmarkError = "Unable to save this bookmark. Please try again.";
                return;
            }
            // A failed save must leave the bookmark icon and active filter unchanged.
            BookmarkError = "";
            IsBookmarked = isBookmarked;
        }
    }
    public record OfferDisplay(string Url, string Text, string Details = "", string Tooltip = "", bool IsWarning = false)
    {
        public bool CanOpenLink { get; init; } = true;
        public string Heading { get; init; } = "";
        public string Price { get; init; } = "";
    }
}
