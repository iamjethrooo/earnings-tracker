using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using EarningsTracker.Commands;
using EarningsTracker.Enums;

namespace EarningsTracker.Prototypes;

public sealed record SampleSession(DateTime Recorded, string Game, decimal Earnings, decimal Hours, string Status)
{
    public string Amount => DashboardViewModel.Usd(Earnings);
    public string Detail => $"{Recorded:MMM d · HH:mm}  ·  {Hours:0.0} h";
    public Brush StatusBrush => (Brush)System.Windows.Application.Current.FindResource(
        Status == "Verified" ? "VerifiedTextColor" : "PendingTextColor");
    public Brush StatusBackground => (Brush)System.Windows.Application.Current.FindResource(
        Status == "Verified" ? "VerifiedBackgroundColor" : "PendingBackgroundColor");
}

public sealed record TrendDay(DateTime Date, decimal Verified, decimal Pending);
public sealed record GameSummary(string Name, int Sessions, decimal Earnings, double Share, ImageSource? Cover)
{
    public string Amount => DashboardViewModel.Usd(Earnings);
    public string Detail => $"{Sessions} verified sessions";
}
public sealed record Metric(string Label, string Value, string Detail, string ColorKey)
{
    public Brush ValueBrush => (Brush)System.Windows.Application.Current.FindResource(ColorKey);
}

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public static readonly DateTime AsOf = new(2026, 10, 19, 23, 59, 0);
    private static readonly string[] GameNames = ["Orbital Drift", "Mosslight Valley", "Neon Circuit", "The Last Outpost"];
    private readonly IReadOnlyList<SampleSession> _sessions = CreateSessions();
    private int _periodIndex = 1;
    private string _state = "Populated";
    private NavigationPage _page = NavigationPage.Dashboard;
    private string? _previewGame;
    private string? _previewDescription;
    public Func<bool>? CanNavigate { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string[] States { get; } = ["Populated", "Empty", "Loading", "Error", "Stale"];
    public ICommand RetryCommand { get; }
    public ICommand GameCommand { get; }
    public ICommand ClosePreviewCommand { get; }
    public ICommand DashboardCommand { get; }
    public ICommand SessionsCommand { get; }

    public DashboardViewModel()
    {
        RetryCommand = new RelayCommand(_ => State = "Populated");
        GameCommand = new RelayCommand(game => OpenGamePreview((string)game!));
        ClosePreviewCommand = new RelayCommand(_ => { _previewGame = null; Notify(); });
        DashboardCommand = new RelayCommand(_ => Page = NavigationPage.Dashboard);
        SessionsCommand = new RelayCommand(_ => Page = NavigationPage.Sessions);
        Refresh();
    }

    public int PeriodIndex
    {
        get => _periodIndex;
        set { if (value is < 0 or > 2 || value == _periodIndex) return; _periodIndex = value; Refresh(); }
    }
    public string State
    {
        get => _state;
        set { if (_state == value) return; _state = value; Refresh(); }
    }
    public NavigationPage Page
    {
        get => _page;
        set
        {
            if (_page != value && CanNavigate?.Invoke() == false) { Notify(); return; }
            _page = value; _previewGame = null; Notify();
        }
    }
    public string PageTitle => Page == NavigationPage.Dashboard ? "Dashboard" : Page.ToString();
    public string PageDescription => Page == NavigationPage.Dashboard
        ? "Your playtime, translated into earnings."
        : IsSessions ? "Inspect recordings, review sources, and make deliberate corrections."
        : IsGames ? "Compare earning opportunities across your PC game library."
        : "Navigation preview · functional integration is deferred.";
    public bool IsDashboard => Page == NavigationPage.Dashboard;
    public bool IsSessions => Page == NavigationPage.Sessions;
    public bool IsGames => Page == NavigationPage.Games;
    public bool ShowPlaceholder => !IsDashboard && !IsSessions && !IsGames;
    public bool ShowDashboard => IsDashboard && State is "Populated" or "Stale";
    public bool ShowState => IsDashboard && !ShowDashboard;
    public bool IsStale => IsDashboard && State == "Stale";
    public bool IsPreviewOpen => _previewGame != null;
    public string PreviewTitle => _previewGame ?? "";
    public string PreviewReturnLabel => IsSessions ? "Back to session" : "Back to overview";
    public string PreviewDescription => _previewDescription ?? $"Synthetic game drill-down · {RangeLabel}\n\n" +
        $"{Games.FirstOrDefault(g => g.Name == _previewGame)?.Amount ?? Usd(0)} verified earnings\n" +
        $"{VisibleSessions.Count(s => s.Game == _previewGame)} sessions in this period\n\n" +
        "This link demonstrates context and navigation only. Game details, editing and real records are outside this iteration.";
    public void OpenGamePreview(string game, string? description = null)
    {
        _previewGame = game; _previewDescription = description; Notify();
    }
    public string PlaceholderText => $"{PageTitle} is outside this Dashboard iteration.\nThe sidebar selection and page heading are live; no production page or workflow is opened.";
    public string StateTitle => State switch
    {
        "Empty" => "A fresh start",
        "Loading" => "Preparing your overview…",
        _ => "Dashboard unavailable"
    };
    public string StateMessage => State switch
    {
        "Empty" => "No sample sessions in this example. Once recordings are available, your earnings and playtime will appear here.",
        "Loading" => "Synthetic loading example. Totals stay hidden until the complete snapshot is ready. Choose Populated to continue.",
        _ => "Simulated read failure. Financial totals are unavailable. Retry restores the synthetic snapshot."
    };
    public bool CanRetry => State == "Error";
    public string RangeLabel => $"{StartDate:MMM d} – {AsOf:MMM d, yyyy}";
    public DateTime StartDate => AsOf.Date.AddDays(1 - new[] { 7, 30, 90 }[PeriodIndex]);
    public IReadOnlyList<SampleSession> VisibleSessions { get; private set; } = [];
    public IReadOnlyList<SampleSession> RecentSessions { get; private set; } = [];
    public IReadOnlyList<TrendDay> Trend { get; private set; } = [];
    public IReadOnlyList<GameSummary> Games { get; private set; } = [];
    public IReadOnlyList<Metric> Metrics { get; private set; } = [];
    public decimal VerifiedTotal { get; private set; }
    public decimal PendingTotal { get; private set; }
    public int VerifiedCount { get; private set; }
    public int PendingCount { get; private set; }
    public string VerifiedAmount => Usd(VerifiedTotal);
    public string PendingAmount => Usd(PendingTotal);
    public string VerifiedShare => $"{VerifiedCount * 100.0 / Math.Max(1, VisibleSessions.Count):0}%";
    public string SessionCount => $"{VisibleSessions.Count} sessions · by recording date";
    public string VerifiedCountLabel => $"Verified · {VerifiedCount}";
    public string PendingCountLabel => $"Pending · {PendingCount}";
    public double VerifiedPercent => VerifiedCount * 100.0 / Math.Max(1, VisibleSessions.Count);
    public string RecentCaption => $"Latest {RecentSessions.Count} of {VisibleSessions.Count} · recording date";

    private void Refresh()
    {
        VisibleSessions = _sessions.Where(s => s.Recorded >= StartDate && s.Recorded <= AsOf).ToArray();
        RecentSessions = VisibleSessions.OrderByDescending(s => s.Recorded).Take(5).ToArray();
        var verified = VisibleSessions.Where(s => s.Status == "Verified").ToArray();
        VerifiedTotal = verified.Sum(s => s.Earnings);
        PendingTotal = VisibleSessions.Where(s => s.Status == "Pending").Sum(s => s.Earnings);
        VerifiedCount = verified.Length;
        PendingCount = VisibleSessions.Count - VerifiedCount;
        Trend = Enumerable.Range(0, (AsOf.Date - StartDate).Days + 1).Select(offset =>
        {
            var day = StartDate.AddDays(offset);
            var sessions = VisibleSessions.Where(s => s.Recorded.Date == day).ToArray();
            return new TrendDay(day, sessions.Where(s => s.Status == "Verified").Sum(s => s.Earnings),
                sessions.Where(s => s.Status == "Pending").Sum(s => s.Earnings));
        }).ToArray();
        var grouped = verified.GroupBy(s => s.Game).OrderByDescending(g => g.Sum(s => s.Earnings)).ToArray();
        var maximum = grouped.Max(g => g.Sum(s => s.Earnings));
        Games = grouped.Select(g => new GameSummary(g.Key, g.Count(), g.Sum(s => s.Earnings),
            (double)(g.Sum(s => s.Earnings) / maximum * 100), CreateCover(Array.IndexOf(GameNames, g.Key)))).ToArray();
        // These are independent illustrative payout records, not an alternative accounting implementation.
        var paid = new[] { (Date: new DateTime(2026, 8, 24), Amount: 248.40m),
            (Date: new DateTime(2026, 9, 21), Amount: 312.80m), (Date: new DateTime(2026, 10, 12), Amount: 284.60m) };
        var payouts = paid.Where(p => p.Date >= StartDate && p.Date <= AsOf).Sum(p => p.Amount);
        Metrics = [
            new("Verified earnings", Usd(VerifiedTotal), $"{VerifiedCount} verified sessions · period", "DefaultTextColor"),
            new("Pending earnings", Usd(PendingTotal), $"{PendingCount} sessions · not yet verified", "PendingTextColor"),
            new("Available balance", Usd(214.62m), "As of Oct 19 · posted after Oct 12", "VerifiedTextColor"),
            new("Payouts", Usd(payouts), "Paid + Processing · payment date", "DefaultTextColor"),
            new("Hours played", $"{VisibleSessions.Sum(s => s.Hours):0.0} h", "Verified + pending · period", "DefaultTextColor")
        ];
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    public static string Usd(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

    private static IReadOnlyList<SampleSession> CreateSessions()
    {
        var sessions = new List<SampleSession>();
        for (var day = 0; day < 90; day++)
        {
            if (day % 9 == 4) continue; // Quiet days make the chart representative rather than uniformly busy.
            for (var slot = 0; slot < 2 + day % 3; slot++)
            {
                var game = (day + slot * 3) % GameNames.Length;
                var hours = 0.5m + (day * 7 + slot * 3) % 18 / 10m;
                var status = day < 5 && (day + slot) % 3 == 0 ? "Pending" : "Verified";
                sessions.Add(new(AsOf.Date.AddDays(-day).AddHours(10 + slot * 3), GameNames[game],
                    decimal.Round(hours * (3.2m + game * 0.6m), 2), hours, status));
            }
        }
        return sessions;
    }

    private static ImageSource? CreateCover(int index)
    {
        if (index == 3) return null; // Exercise the existing missing-cover presenter.
        var colors = new[] { Color.FromRgb(65, 67, 123), Color.FromRgb(43, 91, 77), Color.FromRgb(97, 53, 92) };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(colors[index]), null, new RectangleGeometry(new(0, 0, 48, 60))));
        group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 210, 213, 255)), 2),
            Geometry.Parse(index == 0 ? "M 4,46 L 24,10 44,46 Z M 12,38 L 36,38" : index == 1
                ? "M 0,50 L 15,22 26,40 37,16 48,50" : "M 6,12 L 36,12 12,48 42,48 M 6,30 L 42,30")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
