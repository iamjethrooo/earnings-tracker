using EarningsTracker.Data;
using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Utilities;

namespace EarningsTracker.ViewModels
{
    class GamesViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<GameItemViewModel> Games { get; } = new BatchObservableCollection<GameItemViewModel>();
        public ICollectionView GamesView { get; }
        private string _searchText = "";
        private bool _bookmarkedOnly = false;
        private bool _showArchived = true;
        public bool BookmarkedOnly => _bookmarkedOnly;
        public bool ShowArchived
        {
            get => _showArchived;
            set { if (_showArchived == value) return; _showArchived = value; OnPropertyChanged(nameof(ShowArchived)); ApplyFilters(); }
        }
        public int DefaultSortIndex { get; }
        public string BookmarkError { get; private set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private bool _reloadingCatalog;
        private Task? _loadTask;
        public bool IsLoading { get; private set; }
        public string? LoadError { get; private set; }
        public bool ShowEmptyState => HasLoaded && !IsLoading && LoadError == null && AvailableGamesCount == 0;
        public string EmptyStateText => Games.Count == 0
            ? "No games yet. Refresh catalog to check for available games."
            : "No games match these filters. Adjust your search or filters above.";
        internal bool HasLoaded { get; private set; }
        public List<string> GenreOptions { get; private set; } = ["All genres"];
        public List<string> StoreOptions { get; private set; } = ["All stores"];
        private string _selectedGenre = "All genres", _selectedStore = "All stores";
        private bool _freeOnly, _ownedOnly, _gamePassOnly, _playableOnly;
        public string SelectedGenre { get => _selectedGenre; set { if (_selectedGenre == value) return; _selectedGenre = value; ApplyFilters(); } }
        public string SelectedStore { get => _selectedStore; set { if (_selectedStore == value) return; _selectedStore = value; ApplyFilters(); } }
        public bool FreeOnly { get => _freeOnly; set { if (_freeOnly == value) return; _freeOnly = value; ApplyFilters(); OnPropertyChanged(nameof(FreeOnly)); } }
        public bool OwnedOnly { get => _ownedOnly; set { if (_ownedOnly == value) return; _ownedOnly = value; ApplyFilters(); OnPropertyChanged(nameof(OwnedOnly)); } }
        public bool GamePassOnly { get => _gamePassOnly; set { if (_gamePassOnly == value) return; _gamePassOnly = value; ApplyFilters(); OnPropertyChanged(nameof(GamePassOnly)); } }
        public bool PlayableOnly { get => _playableOnly; set { if (_playableOnly == value) return; _playableOnly = value; ApplyFilters(); OnPropertyChanged(nameof(PlayableOnly)); } }
        public HashSet<string> MySubscriptions { get; private set; } = [];
        public string CatalogStatus { get; private set; } = "";
        public bool ShowPriceSortHelp { get; private set; }
        public string PriceSortHelp => "Lowest price compares saved PH offers and active free/giveaway offers using saved PHP exchange rates, within active/archived groups. Unknown or missing-FX prices sort last. Prices may be outdated; inspect editions and checked dates in details.";
        public string GamePassFilterHelp => "PC Game Pass includes previously verified games. Stale/expired verification does not confirm current availability; Playable now still requires current verification and a selected membership.";

        internal void SetStatus(string text) { CatalogStatus = text; OnPropertyChanged(nameof(CatalogStatus)); }

        private sealed record CatalogSnapshot(Dictionary<int, GameMetadata> Metadata, ILookup<int, GameGenre> Genres,
            ILookup<int, StoreOffer> Offers, ILookup<int, SubscriptionAvailability> Subscriptions,
            Dictionary<int, GameOwnership> Owned, Dictionary<string, ExchangeRate> Rates, HashSet<string> MySubscriptions);

        private CatalogSnapshot ReadCatalog()
        {
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
            var metadata = db.GameMetadata.AsNoTracking().ToDictionary(x => x.GameId);
            var genres = db.GameGenres.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var offers = db.StoreOffers.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var subscriptions = db.SubscriptionAvailability.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var owned = db.GameOwnership.AsNoTracking().ToDictionary(x => x.GameId);
            var rates = db.ExchangeRates.AsNoTracking().ToDictionary(x => x.Currency);
            return new(metadata, genres, offers, subscriptions, owned, rates, new AppSettings(_databaseOptions).Subscriptions());
        }

        internal void ReloadCatalog() => ApplyCatalog(ReadCatalog());
        internal async Task ReloadCatalogAsync() => ApplyCatalog(await Task.Run(ReadCatalog));

        private void ApplyCatalog(CatalogSnapshot snapshot)
        {
            var (metadata, genres, offers, subscriptions, owned, rates, mySubscriptions) = snapshot;
            MySubscriptions = mySubscriptions;
            _reloadingCatalog = true;
            try
            {
                using var deferred = GamesView.DeferRefresh();
                foreach (var item in Games)
                    item.SetCatalog(metadata.GetValueOrDefault(item.Game.Id), genres[item.Game.Id].Select(x => x.Name).ToList(),
                        offers[item.Game.Id].ToList(), subscriptions[item.Game.Id].ToList(), owned.GetValueOrDefault(item.Game.Id), rates);
            }
            finally { _reloadingCatalog = false; }
            GenreOptions = new[] { "All genres" }.Concat(genres.SelectMany(x => x).Select(x => x.Name).Distinct().Order()).ToList();
            StoreOptions = new[] { "All stores" }.Concat(Games.SelectMany(x => x.Offers.Select(o => o.Source)
                .Concat(x.Subscriptions.Where(s => s.Included).Select(s => s.Service))).Distinct().Order()).ToList();
            if (!GenreOptions.Contains(_selectedGenre)) _selectedGenre = "All genres";
            if (!StoreOptions.Contains(_selectedStore)) _selectedStore = "All stores";
            OnPropertyChanged(nameof(GenreOptions)); OnPropertyChanged(nameof(StoreOptions));
            OnPropertyChanged(nameof(SelectedGenre)); OnPropertyChanged(nameof(SelectedStore));
            var active = Games.Where(x => !x.Game.IsArchived).ToList();
            var reviewed = active.Count(x => x.Metadata?.Reviewed == true);
            SetStatus($"{reviewed}/{active.Count} active reviewed · {active.Count - reviewed} unresolved · {Games.Count - active.Count} archived · PH prices · earnings in USD");
            ApplyFilters();
        }

        internal void SaveSubscriptions(IEnumerable<string> services)
        {
            var selected = services.Intersect(CatalogSubscriptions.Services).ToHashSet();
            new AppSettings(_databaseOptions).SaveSubscriptions(selected);
            MySubscriptions = selected; ApplyFilters();
        }
        public GamesViewModel() : this(null, loadImmediately: false) { }

        internal GamesViewModel(DbContextOptions<AppDbContext>? databaseOptions, bool loadImmediately = true)
        {
            _databaseOptions = databaseOptions;
            GamesView = CollectionViewSource.GetDefaultView(Games);
            var settings = new AppSettings(databaseOptions);
            DefaultSortIndex = settings.GamesDefaultSort;
            _bookmarkedOnly = settings.GamesBookmarksOnly;
            _showArchived = settings.GamesShowArchived;
            ApplySortChoice(DefaultSortIndex);
            if (loadImmediately)
            {
                var snapshot = ReadGames();
                PublishGames(snapshot.games, snapshot.bookmarkedIds, ReadCatalog());
            }
        }

        private (List<Game> games, HashSet<int> bookmarkedIds) ReadGames()
        {
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
            return (db.Games.AsNoTracking().ToList(), db.Bookmarks.Select(b => b.GameId).ToHashSet());
        }

        private void PublishGames(List<Game> games, HashSet<int> bookmarkedIds, CatalogSnapshot catalog)
        {
            var rows = games.Select(game => new GameItemViewModel(game, bookmarkedIds.Contains(game.Id), _databaseOptions)).ToList();
            foreach (var item in Games) item.PropertyChanged -= GameItem_PropertyChanged;
            ((BatchObservableCollection<GameItemViewModel>)Games).ReplaceAll(rows);
            foreach (var item in Games) item.PropertyChanged += GameItem_PropertyChanged;
            ApplyCatalog(catalog);
            HasLoaded = true; LoadError = null; OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(BookmarkCount)); OnPropertyChanged(nameof(BookmarkText));
        }

        internal Task LoadGamesAsync() => _loadTask ??= LoadGamesCore();

        private async Task LoadGamesCore()
        {
            await Task.Yield();
            IsLoading = true; LoadError = null; OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(IsLoading)); SetStatus("Loading games…");
            OnPropertyChanged(nameof(AvailableGamesText));
            try
            {
                var snapshot = await Task.Run(() => (games: ReadGames(), catalog: ReadCatalog()));
                PublishGames(snapshot.games.games, snapshot.games.bookmarkedIds, snapshot.catalog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                LoadError = ex.GetBaseException().Message;
                SetStatus("Could not load games. Choose Refresh catalog to retry. " + ex.GetBaseException().Message);
            }
            finally { _loadTask = null; IsLoading = false; OnPropertyChanged(nameof(IsLoading)); OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(AvailableGamesText)); }
        }

        private void GameItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameItemViewModel.IsOwned))
            {
                if (!_reloadingCatalog && (_ownedOnly || _playableOnly)) ApplyFilters();
                return;
            }
            if (e.PropertyName == nameof(GameItemViewModel.IsBookmarked))
            {
                OnPropertyChanged(nameof(BookmarkCount));
                OnPropertyChanged(nameof(BookmarkText));
                if (_bookmarkedOnly) ApplyFilters();
            }
            else if (e.PropertyName == nameof(GameItemViewModel.BookmarkError) && sender is GameItemViewModel item)
            {
                BookmarkError = item.BookmarkError;
                OnPropertyChanged(nameof(BookmarkError));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName)
            );
        }

        public void SortGames(string sortBy, ListSortDirection direction)
        {
            using var deferred = GamesView.DeferRefresh();
            GamesView.SortDescriptions.Clear();
            GamesView.SortDescriptions.Add(
                new SortDescription(
                    nameof(GameItemViewModel.Game) + "." + nameof(Game.IsArchived),
                    ListSortDirection.Ascending
                )
            );

            GamesView.SortDescriptions.Add(
                new SortDescription(sortBy, direction)
            );

            GamesView.SortDescriptions.Add(
                new SortDescription(nameof(GameItemViewModel.Game) + "." + nameof(Game.Id), direction)
            );
        }

        public void ApplySortChoice(int choice)
        {
            ShowPriceSortHelp = choice == 3;
            OnPropertyChanged(nameof(ShowPriceSortHelp));
            switch (choice)
            {
                case 0: SortGames("Game.RatePerHour", ListSortDirection.Descending); break;
                case 1: SortGames("Game.Name", ListSortDirection.Ascending); break;
                case 3: SortGames(nameof(GameItemViewModel.PriceSortValue), ListSortDirection.Ascending); break;
                default: SortGames("Game.Id", ListSortDirection.Descending); break;
            }
        }

        public void SearchGames(string searchText)
        {
            if (_searchText == searchText) return;
            _searchText = searchText;
            ApplyFilters();
        }

        public void FilterBookmarked(bool bookmarkedOnly)
        {
            if (_bookmarkedOnly == bookmarkedOnly) return;
            _bookmarkedOnly = bookmarkedOnly;
            ApplyFilters();
        }

        internal void ClearFilters()
        {
            _searchText = "";
            _selectedGenre = "All genres"; _selectedStore = "All stores";
            _freeOnly = _ownedOnly = _gamePassOnly = _playableOnly = false;
            _bookmarkedOnly = false;
            foreach (var property in new[] { nameof(SelectedGenre), nameof(SelectedStore), nameof(FreeOnly), nameof(OwnedOnly),
                nameof(GamePassOnly), nameof(PlayableOnly), nameof(BookmarkedOnly) }) OnPropertyChanged(property);
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            GamesView.Filter = item =>
            {
                if (item is not GameItemViewModel gameItem)
                    return false;
                if (!_showArchived && gameItem.Game.IsArchived) return false;

                // Search filter
                if (!string.IsNullOrWhiteSpace(_searchText) &&
                    !gameItem.Game.Name.Contains(
                        _searchText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Bookmark filter
                if (_bookmarkedOnly && !gameItem.IsBookmarked)
                    return false;

                if (_selectedGenre != "All genres" && !gameItem.Genres.Contains(_selectedGenre)) return false;
                if (_selectedStore != "All stores" && !gameItem.Offers.Any(x => x.Source == _selectedStore)
                    && !gameItem.Subscriptions.Any(x => x.Included && x.Service == _selectedStore)) return false;
                if (_freeOnly && !gameItem.FreeToPlay) return false;
                if (_ownedOnly && !gameItem.IsOwned) return false;
                var now = DateTime.UtcNow;
                if (_gamePassOnly && (CatalogPresentation.GamePassEntry(gameItem.Subscriptions) is not { } gamePass
                    || !CatalogPresentation.PreviouslyIncluded(gamePass, now))) return false;
                if (_playableOnly && !gameItem.IsOwned && !gameItem.Offers.Any(x => CatalogPresentation.IsFree(x, now))
                    && !gameItem.Subscriptions.Any(s => CatalogPresentation.Current(s, now) && CatalogSubscriptions.Includes(MySubscriptions, s.Service))) return false;

                return true;
            };

            // Setting Filter already refreshes the view; cache the count once for both labels.
            _availableGamesCount = GamesView.Cast<GameItemViewModel>().Count();
            OnPropertyChanged(nameof(AvailableGamesCount));
            OnPropertyChanged(nameof(AvailableGamesText));
            OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(EmptyStateText));
        }

        public int BookmarkCount => Games.Count(game => game.IsBookmarked);
        public string BookmarkText => $"My Bookmarks ({BookmarkCount})";

        private int _availableGamesCount;
        public int AvailableGamesCount => _availableGamesCount;
        public string AvailableGamesText
        {
            get
            {
                if (!HasLoaded && IsLoading) return "Loading games…";
                if (!HasLoaded && LoadError != null) return "Games unavailable";
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    return $"{AvailableGamesCount} game{(AvailableGamesCount != 1 ? "s" : "")} matching \"{_searchText}\"";
                }
                return $"{AvailableGamesCount} game{(AvailableGamesCount != 1 ? "s" : "")}";
            }
        }
    }
}
