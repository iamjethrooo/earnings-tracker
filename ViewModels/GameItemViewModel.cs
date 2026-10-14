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
        public bool IsOwned => Ownership?.IsOwned == true;
        public string OwnershipButtonText => IsOwned ? "Owned" : "Mark as owned";
        public string OwnershipTooltip => IsOwned ? "Remove from owned games. Saved purchase cost is kept."
            : "Add to owned games. Edit purchase cost in game details.";
        public string OwnershipError { get; private set; } = "";
        public ICommand ToggleOwnedCommand { get; }
        public string GenreText => Genres.Count == 0 ? "Genre unknown" : string.Join(" · ", Genres);
        public string SourceText => Offers.Count == 0 && Subscriptions.Count == 0 ? "Sources unknown"
            : string.Join(" · ", Offers.Select(x => x.Source).Concat(Subscriptions.Where(x => x.Included).Select(x => x.Service)).Distinct());
        public string Summary => string.IsNullOrWhiteSpace(Metadata?.Summary) ? "Metadata has not been reviewed yet." : Metadata.Summary;
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
                $"Checked {x.VerifiedAt:MMM d, yyyy}"
                + (x.Edition == "Base game" ? "" : $" · {x.Edition}"), CatalogPresentation.OfferText(x, Rates, DateTime.UtcNow))).ToList();
        public List<OfferDisplay> SubscriptionDetails => Subscriptions.Select(x => new OfferDisplay(x.Url,
            $"{x.Service} · {(x.Included ? "Included" : "Not included")} · {x.Platform}/{x.Region} · checked {x.VerifiedAt:MMM d, yyyy}"
            + (CatalogPresentation.Current(x, DateTime.UtcNow) ? "" : " · expired, stale, or unavailable"))).ToList();

        internal void SetCatalog(GameMetadata? metadata, List<string> genres, List<StoreOffer> offers,
            List<SubscriptionAvailability> subscriptions, GameOwnership? ownership, IReadOnlyDictionary<string, ExchangeRate> rates)
        {
            var artworkChanged = Metadata?.CoverSha256 != metadata?.CoverSha256 || Metadata?.CoverUrl != metadata?.CoverUrl;
            Metadata = metadata; Genres = genres;
            Offers = offers.Where(x => CatalogSources.PopularName(x.Source) != null).ToList();
            foreach (var offer in Offers) offer.Source = CatalogSources.PopularName(offer.Source)!;
            Subscriptions = subscriptions; Ownership = ownership; Rates = rates;
            if (artworkChanged) { _cover = null; _coverRequested = false; OnPropertyChanged(nameof(Cover)); }
            foreach (var property in new[] { nameof(Metadata), nameof(Genres), nameof(Offers), nameof(Subscriptions), nameof(Rates),
                nameof(GenreText), nameof(SourceText), nameof(Summary), nameof(MetadataStatus), nameof(FreeToPlay),
                nameof(PriceSortValue), nameof(LowestPrice), nameof(PriceText), nameof(PriceTooltip), nameof(AccessStatusText),
                nameof(OfferDetails), nameof(SubscriptionDetails) }) OnPropertyChanged(property);
            NotifyOwnership();
        }

        private void NotifyOwnership()
        {
            foreach (var property in new[] { nameof(Ownership), nameof(IsOwned), nameof(OwnershipButtonText),
                nameof(OwnershipTooltip), nameof(BreakEvenText) }) OnPropertyChanged(property);
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
    public record OfferDisplay(string Url, string Text, string Details = "", string Tooltip = "");
}
