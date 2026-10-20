using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using EarningsTracker.Commands;

namespace EarningsTracker.Prototypes.Games;

public sealed class GameLibraryViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<GameLibraryItem> _games = new(CreateGames());
    private GameLibraryItem? _selected;
    private string _search = "", _genre = "All genres", _access = "All access", _store = "All stores", _sort = "Newest", _example = "Populated";
    private bool _bookmarksOnly, _showArchived = true, _detailsOpen, _preferencesOpen;
    private string _startingSort = "Newest", _history = "";
    private bool _startingBookmarks, _startingArchived = true;
    private bool _isCompact;
    public ICollectionView Games { get; }
    public string[] Genres { get; } = ["All genres", "Racing", "Adventure", "Strategy", "Simulation", "Action"];
    public string[] AccessOptions { get; } = ["All access", "Owned", "Playable now", "Free-to-play", "PC Game Pass", "Price unavailable", "Stale information"];
    public string[] Stores { get; } = ["All stores", "Steam", "Epic", "GOG"];
    public string[] Sorts { get; } = ["Newest", "Highest Pay", "Game A–Z", "Lowest price"];
    public string[] Examples { get; } = ["Populated", "Archived", "Missing artwork", "Unavailable price", "Loading", "Filtered empty", "Stale", "Error"];
    public RelayCommand ToggleBookmarkCommand { get; }
    public RelayCommand ToggleOwnedCommand { get; }
    public RelayCommand DetailsCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand PreferencesCommand { get; }
    public RelayCommand ClosePreferencesCommand { get; }
    public RelayCommand ApplyStartingCommand { get; }
    public RelayCommand HistoryCommand { get; }
    public RelayCommand CloseHistoryCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public GameLibraryViewModel()
    {
        Games = CollectionViewSource.GetDefaultView(_games);
        Games.Filter = Matches;
        foreach (var game in _games) game.PropertyChanged += (_, _) => Refresh();
        ToggleBookmarkCommand = new(value => { if (value is GameLibraryItem item) item.IsBookmarked = !item.IsBookmarked; });
        ToggleOwnedCommand = new(value => { if (value is GameLibraryItem item) item.IsOwned = !item.IsOwned; });
        DetailsCommand = new(value => { if (value is GameLibraryItem item) { _selected = item; _detailsOpen = true; Notify(); } });
        BackCommand = new(_ =>
        {
            _detailsOpen = false; _history = "";
            if (_selected != null && !Games.Contains(_selected)) _selected = null;
            Notify();
        });
        ClearCommand = new(_ => ClearFilters());
        RetryCommand = new(_ => { _example = "Populated"; Refresh(); });
        PreferencesCommand = new(_ => { _preferencesOpen = true; Notify(); });
        ClosePreferencesCommand = new(_ => { _preferencesOpen = false; Notify(); });
        ApplyStartingCommand = new(_ => ApplyStarting());
        HistoryCommand = new(value => { _history = (string)value!; Notify(); });
        CloseHistoryCommand = new(_ => { _history = ""; Notify(); });
        ApplySort();
    }
    public string Search { get => _search; set { _search = value; Refresh(); } }
    public string Genre { get => _genre; set { _genre = value; Refresh(); } }
    public string Access { get => _access; set { _access = value; Refresh(); } }
    public string Store { get => _store; set { _store = value; Refresh(); } }
    public string Sort { get => _sort; set { _sort = value; ApplySort(); Notify(); } }
    public bool BookmarksOnly { get => _bookmarksOnly; set { _bookmarksOnly = value; Refresh(); } }
    public bool ShowArchived { get => _showArchived; set { _showArchived = value; Refresh(); } }
    public string Example
    {
        get => _example;
        set { _example = value; _detailsOpen = false; Refresh(); }
    }
    public GameLibraryItem? SelectedGame { get => _selected; set { _selected = value; Notify(); } }
    public bool IsDetailsOpen => _detailsOpen && _selected != null;
    public bool IsCompact
    {
        get => _isCompact;
        set { if (_isCompact == value) return; _isCompact = value; PropertyChanged?.Invoke(this, new(nameof(IsCompact))); }
    }
    public bool ShowLibrary => !IsDetailsOpen;
    public bool IsPreferencesOpen => _preferencesOpen;
    public bool IsHistoryOpen => _history.Length > 0;
    public bool IsStale => Example == "Stale";
    public int VisibleCount => Games.Cast<GameLibraryItem>().Count();
    public int TotalCount => _games.Count;
    public bool ShowState => VisibleCount == 0;
    public bool CanRetry => Example is "Error" or "Stale";
    public bool CanClear => Example == "Filtered empty" || (Example is not ("Loading" or "Error") && VisibleCount == 0);
    public string CountLabel => Example == "Loading" ? "Loading sample library…" : Example == "Error" ? "Library unavailable" : $"{VisibleCount} of {TotalCount} games";
    public string BasisLabel => "PC / PH · earnings USD · acquisition currencies retained · synthetic data";
    public bool ShowPriceSortHelp => Sort == "Lowest price";
    public string PriceSortHelp => "Within active / archived groups: current PH primary offers by PHP equivalent; USD uses sample FX 56.25 PHP/USD (Oct 19). Stale, unknown, unavailable and missing-FX offers sort last together. Other editions appear in details, outside this ranking.";
    public string StateTitle => Example == "Loading" ? "Loading game library…" : Example == "Error" ? "Library unavailable" : "No games match";
    public string StateMessage => Example == "Loading" ? "Static loading example. The library will appear after a complete sample snapshot is ready."
        : Example == "Error" ? "Simulated read failure. Retry restores the synthetic library without accessing any service."
        : "Try another search or access filter. Clear filters to return to the sample library.";
    public string StartingSort { get => _startingSort; set { _startingSort = value; Notify(); } }
    public bool StartingBookmarks { get => _startingBookmarks; set { _startingBookmarks = value; Notify(); } }
    public bool StartingArchived { get => _startingArchived; set { _startingArchived = value; Notify(); } }
    public string StartingSummary => $"Starting view: {StartingSort} · bookmarks {(StartingBookmarks ? "only" : "off")} · archived {(StartingArchived ? "shown" : "hidden")}";
    public string SelectionSummary => SelectedGame == null ? "Select a row · open its title for details" : $"Selected: {SelectedGame.Name} · open title or press Enter for details";
    public string HistoryTitle => _history;
    public string HistoryDescription => _history switch
    {
        "Session history" => $"{SelectedGame?.Name}\n\nOct 18 · Verified · 60 min · $4.20\nOct 17 · Pending · 45 min · $3.15\nOct 15 · Verified · 30 min · $2.10\n\nIllustrative recorded values, not recalculated from the current game rate. The Sessions prototype and this fixture remain separate.",
        "Rate history" => "Oct 1, 2026 · current sample rate version\nSep 1, 2026 · historical rate version retained\n\nChanging a current rate must not rewrite earnings recorded under an earlier version. This preview offers no mutation.",
        _ => "Oct 1, 2026 · sample cycle began\nSep 1, 2026 · prior cap-reset boundary\n\nHistorical records stay linked to their original identities and cycles. No reset action is available in this prototype."
    };
    public void CloseOverlays()
    {
        _preferencesOpen = false; _history = ""; Notify();
    }
    private bool Matches(object value)
    {
        var item = (GameLibraryItem)value;
        if (Example is "Loading" or "Error" or "Filtered empty") return false;
        if (!ShowArchived && item.IsArchived || BookmarksOnly && !item.IsBookmarked) return false;
        if (Search.Trim().Length > 0 && !item.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (Genre != "All genres" && item.Game.Genre != Genre) return false;
        if (Store != "All stores" && !item.Offers.Any(o => o.Store == Store)) return false;
        if (Example == "Archived" && !item.IsArchived || Example == "Missing artwork" && item.Cover != null || Example == "Unavailable price" && !item.PriceUnknown) return false;
        return Access switch
        {
            "Owned" => item.IsOwned, "Playable now" => item.IsPlayable, "Free-to-play" => item.PrimaryOffer.IsFree,
            "PC Game Pass" => item.Game.SubscriptionCurrent, "Price unavailable" => item.PriceUnknown,
            "Stale information" => item.HasStaleInformation, _ => true
        };
    }
    private void Refresh()
    {
        var selected = _selected;
        Games.Refresh();
        // Filtering a bookmark/owned row out must not remove an already open details context.
        _selected = _detailsOpen ? selected : selected != null && Games.Contains(selected) ? selected : null;
        Notify();
    }
    private void ApplySort()
    {
        using (Games.DeferRefresh())
        {
            Games.SortDescriptions.Clear();
            Games.SortDescriptions.Add(new(nameof(GameLibraryItem.IsArchived), ListSortDirection.Ascending));
            Games.SortDescriptions.Add(Sort switch
            {
                "Highest Pay" => new(nameof(GameLibraryItem.Rate), ListSortDirection.Descending),
                "Game A–Z" => new(nameof(GameLibraryItem.Name), ListSortDirection.Ascending),
                "Lowest price" => new(nameof(GameLibraryItem.PriceSortValue), ListSortDirection.Ascending),
                _ => new(nameof(GameLibraryItem.Added), ListSortDirection.Descending)
            });
            Games.SortDescriptions.Add(new("Game.Id", ListSortDirection.Descending));
        }
    }
    public void ClearFilters()
    {
        _search = ""; _genre = "All genres"; _access = "All access"; _store = "All stores"; _bookmarksOnly = false;
        _example = "Populated";
        Refresh(); // Archive visibility and the chosen sort remain deliberate preferences.
    }
    private void ApplyStarting()
    {
        _sort = StartingSort; _bookmarksOnly = StartingBookmarks; _showArchived = StartingArchived;
        _search = ""; _genre = "All genres"; _access = "All access"; _store = "All stores"; _example = "Populated";
        _detailsOpen = false; _preferencesOpen = false; ApplySort(); Refresh();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static IEnumerable<GameLibraryItem> CreateGames()
    {
        string[] names = ["Orbital Drift", "Mosslight Valley", "Neon Circuit", "The Last Outpost", "Sable Atlas", "Juniper Hollow", "Glass Sea", "Rift Chronicle", "Ashen Harbor", "Copper Skies", "Signal Garden", "Echo Frontier"];
        string[] adjectives = ["Amber", "Silver", "Silent", "Ember", "Lunar", "Velvet", "Cloud", "Iron", "Paper", "Distant", "Violet", "Golden"];
        string[] nouns = ["Harbor", "Summit", "Orchard", "Horizon", "Passage", "Islands", "Circuit", "Odyssey", "Woods", "Station", "Tides", "Fields", "Outpost", "Signal", "Valley"];
        string[] genres = ["Racing", "Adventure", "Action", "Strategy", "Simulation"];
        var day = DashboardViewModel.AsOf.Date;
        for (var i = 0; i < 180; i++)
        {
            var archived = i == 3 || i > 12 && i % 17 == 0;
            var name = i < names.Length ? names[i] : $"{adjectives[i / 15]} {nouns[i % 15]}";
            var rate = 3.20m + (i * 7 % 21) / 10m;
            var cap = 8m + i % 5 * 2;
            var used = i == 10 ? cap + 2 : i % 7 * 0.75m;
            var unknown = i == 4 || i % 19 == 0 && i > 0;
            var stale = i == 6 || i > 12 && i % 13 == 0;
            var currency = i == 5 ? "USD" : i == 7 ? "EUR" : "PHP";
            decimal? amount = unknown ? null : i == 2 || i % 11 == 0 && i > 0 ? 0 : currency == "PHP" ? 349m + i % 9 * 150m : 19.99m;
            var store = i % 3 == 0 ? "Steam" : i % 3 == 1 ? "GOG" : "Epic";
            var primary = new SampleOffer(store, i == 7 ? "Complete edition" : "Base game", amount, currency,
                i == 9 ? "US" : "PH", day.AddDays(stale ? -17 : -1), stale, Available: i != 8);
            var offers = new[] { primary, new SampleOffer("Steam", "Deluxe edition", unknown ? null : 1499m, "PHP", "PH", day.AddDays(-2)) };
            DateTime? subscription = i == 1 || i > 12 && i % 9 == 0 ? day.AddDays(-2) : i == 6 ? day.AddDays(-17) : null;
            var owned = i == 0 || i > 11 && i % 4 == 0;
            var game = new SampleLibraryGame(180 - i, name, genres[i % genres.Length], rate, cap, used,
                decimal.Round(used * rate, 2), rate * cap, day.AddDays(-i), archived,
                i == 4 || i % 23 == 0 && i > 0 ? null : CreateCover(i), offers, "PC Game Pass", subscription,
                i % 4 == 0 ? "Steam ownership · demo owned" : i % 4 == 1 ? "Steam ownership · demo not owned" : "Steam ownership · not connected",
                i % 4 == 0 ? "Installed · demo indicator" : "Installed · unknown (demo slot)",
                owned ? 599m : null, "PHP");
            yield return new(game, owned, i < 3 || i % 8 == 0);
        }
    }
    private static ImageSource CreateCover(int index)
    {
        Color[] colors = [Color.FromRgb(60, 64, 119), Color.FromRgb(42, 83, 67), Color.FromRgb(99, 49, 83), Color.FromRgb(90, 67, 46)];
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[index % 4]), null, new RectangleGeometry(new(0, 0, 56, 72))));
        var ink = new SolidColorBrush(Color.FromArgb(170, 218, 220, 245));
        drawing.Children.Add(new GeometryDrawing(null, new Pen(ink, 1.5), Geometry.Parse((index % 3) switch
        {
            0 => "M 4,54 L 28,12 52,54 Z M 12,44 L 44,44 M 28,12 L 28,60",
            1 => "M 0,56 L 18,28 30,46 42,14 56,56 M 8,62 L 48,62",
            _ => "M 10,18 L 46,18 12,56 48,56 M 4,36 L 52,36"
        })));
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
