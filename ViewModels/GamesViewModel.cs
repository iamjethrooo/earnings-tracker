using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
        public bool BookmarkedOnly
        {
            get => _bookmarkedOnly;
            set { if (_bookmarkedOnly == value) return; _bookmarkedOnly = value; OnPropertyChanged(nameof(BookmarkedOnly)); ApplyFilters(); }
        }
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
        public string EmptyStateText => _installedOnly && !Installations.IsConfigured
            ? "Enable installation sources in Settings or open Local installations to learn more."
            : Games.Count == 0
                ? "No games yet. Open Updates to check for game list updates."
                : "No games match these filters. Adjust your search or filters above.";
        internal bool HasLoaded { get; private set; }
        public List<string> GenreOptions { get; private set; } = ["All genres"];
        public List<string> StoreOptions { get; private set; } = ["All stores"];
        private string _selectedGenre = "All genres", _selectedStore = "All stores";
        private bool _freeOnly, _ownedOnly, _gamePassOnly, _playableOnly, _steamOnly, _multiplayerOnly, _installedOnly, _timeLeftOnly;
        public string SelectedGenre { get => _selectedGenre; set { if (_selectedGenre == value) return; _selectedGenre = value; ApplyFilters(); } }
        public string SelectedStore { get => _selectedStore; set { if (_selectedStore == value) return; _selectedStore = value; ApplyFilters(); } }
        public bool FreeOnly { get => _freeOnly; set { if (_freeOnly == value) return; _freeOnly = value; ApplyFilters(); OnPropertyChanged(nameof(FreeOnly)); } }
        public bool OwnedOnly { get => _ownedOnly; set { if (_ownedOnly == value) return; _ownedOnly = value; ApplyFilters(); OnPropertyChanged(nameof(OwnedOnly)); } }
        public bool SteamOnly { get => _steamOnly; set { if (_steamOnly == value) return; _steamOnly = value; ApplyFilters(); OnPropertyChanged(nameof(SteamOnly)); } }
        public bool InstalledOnly { get => _installedOnly; set { if (_installedOnly == value) return; _installedOnly = value; ApplyFilters(); OnPropertyChanged(nameof(InstalledOnly)); } }
        public bool TimeLeftOnly { get => _timeLeftOnly; set { if (_timeLeftOnly == value) return; _timeLeftOnly = value; ApplyFilters(); OnPropertyChanged(nameof(TimeLeftOnly)); } }
        public bool GamePassOnly { get => _gamePassOnly; set { if (_gamePassOnly == value) return; _gamePassOnly = value; ApplyFilters(); OnPropertyChanged(nameof(GamePassOnly)); } }
        public bool PlayableOnly { get => _playableOnly; set { if (_playableOnly == value) return; _playableOnly = value; ApplyFilters(); OnPropertyChanged(nameof(PlayableOnly)); } }
        public bool MultiplayerOnly { get => _multiplayerOnly; set { if (_multiplayerOnly == value) return; _multiplayerOnly = value; ApplyFilters(); OnPropertyChanged(nameof(MultiplayerOnly)); } }
        // Clearing preserves the separate archived-visibility preference and current sort.
        public bool CanClearFilters => !string.IsNullOrEmpty(_searchText) || _bookmarkedOnly
            || _selectedGenre != "All genres" || _selectedStore != "All stores"
            || _freeOnly || _ownedOnly || _gamePassOnly || _playableOnly || _steamOnly || _multiplayerOnly || _installedOnly || _timeLeftOnly;
        public int SecondaryFilterCount => (_freeOnly ? 1 : 0) + (_steamOnly ? 1 : 0)
            + (_gamePassOnly ? 1 : 0) + (_showArchived ? 1 : 0);
        public string MoreFiltersText => SecondaryFilterCount == 0 ? "More filters" : $"More filters ({SecondaryFilterCount})";
        public HashSet<string> MySubscriptions { get; private set; } = [];
        public string CatalogStatus { get; private set; } = "";
        public string SteamStatus { get; private set; } = "";
        internal LocalInstallationSnapshot Installations { get; private set; } = new(false, [], null);
        public bool ShowPriceSortHelp { get; private set; }
        public bool ShowGrindingRatings { get; private set; }
        public bool ShowReplayRatings { get; private set; }
        public string PriceSortHelp => "Saved PH prices · may be outdated";
        public string GamePassFilterHelp => "PC Game Pass includes previously verified games. Stale/expired verification does not confirm current availability; Playable now still requires current verification and a selected membership.";

        internal void SetStatus(string text) { CatalogStatus = text; OnPropertyChanged(nameof(CatalogStatus)); }

        private sealed record CatalogSnapshot(Dictionary<int, GameMetadata> Metadata, ILookup<int, GameGenre> Genres,
            ILookup<int, StoreOffer> Offers, ILookup<int, SubscriptionAvailability> Subscriptions,
            Dictionary<int, GameOwnership> Owned, Dictionary<string, ExchangeRate> Rates, HashSet<string> MySubscriptions,
            SteamLibrarySnapshot? Steam, string SteamStatus, bool SteamStale, CatalogService.SteamIdentityDocument? SteamIdentities,
            LocalInstallationSnapshot Installations);

        private CatalogSnapshot ReadCatalog()
        {
            var settings = new AppSettings(_databaseOptions);
            var mySubscriptions = settings.Subscriptions();
            var installations = LocalGameInstallations.Read(settings.Get(SteamInstallations.SettingsKey),
                settings.Get(LocalGameInstallations.FoldersKey), settings.Get(LocalGameInstallations.WindowsKey) == "true");
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
            using var transaction = db.Database.BeginTransaction();
            var metadata = db.GameMetadata.AsNoTracking().ToDictionary(x => x.GameId);
            var genres = db.GameGenres.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var offers = db.StoreOffers.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var subscriptions = db.SubscriptionAvailability.AsNoTracking().ToList().ToLookup(x => x.GameId);
            var owned = db.GameOwnership.AsNoTracking().ToDictionary(x => x.GameId);
            var rates = db.ExchangeRates.AsNoTracking().ToDictionary(x => x.Currency);
            var steam = SteamLibrary.Read(db);
            var steamStatus = SteamLibrary.Status(db, steam);
            CatalogService.SteamIdentityDocument? steamIdentities = null;
            if (steam != null || installations.SteamAppIds != null)
            {
                try { steamIdentities = CatalogService.ReadSteamIdentities(db); }
                catch (InvalidDataException)
                {
                    // A damaged mapping document must not re-enable legacy matching or hide the game list.
                    steam = null;
                    installations = installations with { SteamAppIds = null };
                    steamStatus += " · Catalog Steam mappings unavailable.";
                }
            }
            return new(metadata, genres, offers, subscriptions, owned, rates, mySubscriptions,
                steam, steamStatus, steam != null && SteamLibrary.IsStale(db, steam), steamIdentities, installations);
        }

        internal void ReloadCatalog() => ApplyCatalog(ReadCatalog());
        internal async Task ReloadCatalogAsync()
        {
            var ownershipAtStart = Games.ToDictionary(item => item.Game.Id, item => item.Ownership);
            var snapshot = await Task.Run(ReadCatalog);
            foreach (var item in Games)
            {
                // Keep an ownership edit saved while the background catalog read was in flight.
                if (item.Ownership is { } ownership && ownershipAtStart.TryGetValue(item.Game.Id, out var previous)
                    && !ReferenceEquals(ownership, previous))
                    snapshot.Owned[item.Game.Id] = ownership;
            }
            ApplyCatalog(snapshot);
        }

        internal void UpdateCycleProgress(Game game, decimal minutes)
        {
            var item = Games.FirstOrDefault(row => row.Game.Id == game.Id);
            if (item == null) return;
            item.Game.CapResetDate = game.CapResetDate;
            item.SetCycleMinutes(minutes);
            if (_timeLeftOnly) ApplyFilters();
        }

        private void ApplyCatalog(CatalogSnapshot snapshot)
        {
            var (metadata, genres, offers, subscriptions, owned, rates, mySubscriptions, steam, steamStatus, steamStale, steamIdentities, installations) = snapshot;
            MySubscriptions = mySubscriptions;
            Installations = installations;
            var localNames = installations.Entries.Where(entry => entry.SteamAppId == null && entry.HasLocalFolder)
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Source, StringComparer.OrdinalIgnoreCase);
            var uniqueGameNames = Games.GroupBy(item => item.Game.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _reloadingCatalog = true;
            try
            {
                using var deferred = GamesView.DeferRefresh();
                foreach (var item in Games)
                {
                    var game = item.Game;
                    var gameMetadata = metadata.GetValueOrDefault(game.Id);
                    var gameOffers = offers[game.Id].ToList();
                    var installationSource = SteamGameMatching.MatchAppIds(gameMetadata, gameOffers,
                        installations.SteamAppIds, steamIdentities) == SteamMatch.Matched
                        ? LocalGameInstallations.SteamSource
                        : gameMetadata?.Reviewed == true && uniqueGameNames.Contains(game.Name)
                            ? localNames.GetValueOrDefault(game.Name)
                            : null;
                    item.SetCatalog(gameMetadata, genres[game.Id].Select(x => x.Name).ToList(), gameOffers,
                        subscriptions[game.Id].ToList(), owned.GetValueOrDefault(game.Id), rates,
                        SteamGameMatching.Match(gameMetadata, gameOffers, steam, steamIdentities),
                        steam?.SyncedAtUtc, steamStale, installationSource, installations.IsConfigured);
                }
                GameRatingSuggestions.Apply(Games);
            }
            finally { _reloadingCatalog = false; }
            SteamStatus = steamStatus;
            if (steam != null)
            {
                var matched = Games.Count(game => game.SteamMatch == SteamMatch.Matched);
                var ambiguous = Games.Count(game => game.SteamMatch == SteamMatch.Ambiguous);
                var unmapped = Games.Count(game => game.SteamMatch == SteamMatch.Unmapped);
                var notObserved = Games.Count(game => game.SteamMatch == SteamMatch.NotObserved);
                SteamStatus += $" · {matched} matched · {unmapped + notObserved} unmatched ({unmapped} without reviewed AppIDs, {notObserved} not reported) · {ambiguous} ambiguous";
            }
            OnPropertyChanged(nameof(SteamStatus));
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
                PublishGames(snapshot, ReadCatalog());
            }
        }

        private sealed record GamesSnapshot(List<Game> Games, HashSet<int> BookmarkedIds,
            Dictionary<int, decimal> CycleMinutes, Dictionary<int, PersonalGameRatings> Ratings);

        private GamesSnapshot ReadGames()
        {
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
            var games = db.Games.AsNoTracking().ToList();
            var resetDates = games.ToDictionary(game => game.Id, game => game.CapResetDate?.Date);
            var cycleMinutes = new Dictionary<int, decimal>();
            var verifiedSessions = db.Sessions.AsNoTracking()
                .Where(session => session.GameId.HasValue && session.Status == SessionStatus.Verified)
                .Select(session => new { session.GameId, session.Duration, session.Date, session.RecordedDate })
                .ToList();

            foreach (var session in verifiedSessions)
            {
                var gameId = session.GameId!.Value;
                if (!resetDates.TryGetValue(gameId, out var resetDate)) continue;
                var recordedDate = (session.RecordedDate == default ? session.Date : session.RecordedDate).Date;
                if (resetDate.HasValue && recordedDate < resetDate.Value) continue;
                cycleMinutes[gameId] = cycleMinutes.GetValueOrDefault(gameId) + (decimal)session.Duration.TotalMinutes;
            }

            return new(games, db.Bookmarks.Select(b => b.GameId).ToHashSet(), cycleMinutes, AppSettings.ReadGameRatings(db));
        }

        private void PublishGames(GamesSnapshot snapshot, CatalogSnapshot catalog)
        {
            var rows = snapshot.Games.Select(game => new GameItemViewModel(game, snapshot.BookmarkedIds.Contains(game.Id), _databaseOptions)).ToList();
            foreach (var row in rows)
            {
                row.SetCycleMinutes(snapshot.CycleMinutes.GetValueOrDefault(row.Game.Id));
                row.SetRatings(snapshot.Ratings.GetValueOrDefault(row.Game.Id));
            }
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
                PublishGames(snapshot.games, snapshot.catalog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                LoadError = ex.GetBaseException().Message;
                SetStatus("Could not load games. Use the Updates menu to retry. " + ex.GetBaseException().Message);
            }
            finally { _loadTask = null; IsLoading = false; OnPropertyChanged(nameof(IsLoading)); OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(AvailableGamesText)); }
        }

        private void GameItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_reloadingCatalog && e.PropertyName == nameof(GameItemViewModel.Ratings))
            {
                GameRatingSuggestions.Apply(Games);
                if (ShowGrindingRatings || ShowReplayRatings) GamesView.Refresh();
                return;
            }
            if (e.PropertyName == nameof(GameItemViewModel.IsOwned))
            {
                if (!_reloadingCatalog && (_ownedOnly || _playableOnly || _steamOnly)) ApplyFilters();
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
            ShowGrindingRatings = choice == 4;
            ShowReplayRatings = choice == 5;
            OnPropertyChanged(nameof(ShowPriceSortHelp));
            OnPropertyChanged(nameof(ShowGrindingRatings));
            OnPropertyChanged(nameof(ShowReplayRatings));
            switch (choice)
            {
                case 0: SortGames("Game.RatePerHour", ListSortDirection.Descending); break;
                case 1: SortGames("Game.Name", ListSortDirection.Ascending); break;
                case 3: SortGames(nameof(GameItemViewModel.PriceSortValue), ListSortDirection.Ascending); break;
                case 4: SortByRating(nameof(GameItemViewModel.GrindingRank)); break;
                case 5: SortByRating(nameof(GameItemViewModel.ReplayRank)); break;
                default: SortGames("Game.Id", ListSortDirection.Descending); break;
            }
        }

        private void SortByRating(string rating)
        {
            using var deferred = GamesView.DeferRefresh();
            GamesView.SortDescriptions.Clear();
            GamesView.SortDescriptions.Add(new SortDescription(rating, ListSortDirection.Descending));
            GamesView.SortDescriptions.Add(new SortDescription("Game.Name", ListSortDirection.Ascending));
            GamesView.SortDescriptions.Add(new SortDescription("Game.Id", ListSortDirection.Ascending));
        }

        public void SearchGames(string searchText)
        {
            if (_searchText == searchText) return;
            _searchText = searchText;
            ApplyFilters();
        }

        public void FilterBookmarked(bool bookmarkedOnly) => BookmarkedOnly = bookmarkedOnly;

        internal void ClearFilters()
        {
            _searchText = "";
            _selectedGenre = "All genres"; _selectedStore = "All stores";
            _freeOnly = _ownedOnly = _gamePassOnly = _playableOnly = _steamOnly = _multiplayerOnly = _installedOnly = _timeLeftOnly = false;
            _bookmarkedOnly = false;
            foreach (var property in new[] { nameof(SelectedGenre), nameof(SelectedStore), nameof(FreeOnly), nameof(OwnedOnly),
                nameof(GamePassOnly), nameof(PlayableOnly), nameof(SteamOnly), nameof(BookmarkedOnly), nameof(MultiplayerOnly),
                nameof(InstalledOnly), nameof(TimeLeftOnly) }) OnPropertyChanged(property);
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
                if (_steamOnly && !gameItem.IsSteamOwned) return false;
                if (_installedOnly && !gameItem.IsInstalled) return false;
                if (_timeLeftOnly && !gameItem.HasTimeLeftToEarn) return false;
                if (_multiplayerOnly && gameItem.Metadata is not { Reviewed: true, SupportsMultiplayer: true }) return false;
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
            OnPropertyChanged(nameof(CanClearFilters));
            OnPropertyChanged(nameof(SecondaryFilterCount));
            OnPropertyChanged(nameof(MoreFiltersText));
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
