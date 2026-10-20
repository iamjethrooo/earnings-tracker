using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Enums;
using EarningsTracker.Utilities;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EarningsTracker.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// </summary>
    public partial class DashboardView : UserControl, INotifyPropertyChanged
    {
        private decimal _totalEarned;
        private decimal _totalPaidOut;
        private decimal _balance;
        private string _balanceDescription = string.Empty;
        private int _totalSessions;
        private double _totalHoursPlayed;
        public decimal TotalEarned { get => _totalEarned; private set { _totalEarned = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalEarned))); } }
        public decimal TotalPaidOut { get => _totalPaidOut; private set { _totalPaidOut = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPaidOut))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalPaidOutDisplay))); } }
        public string TotalPaidOutDisplay => TotalPaidOut.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        public decimal Balance { get => _balance; private set { _balance = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Balance))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BalanceDisplay))); } }
        public string BalanceDisplay => Balance.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        public string BalanceDescription { get => _balanceDescription; private set { _balanceDescription = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BalanceDescription))); } }
        public int TotalSessions { get => _totalSessions; private set { _totalSessions = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSessions))); } }
        public double TotalHoursPlayed { get => _totalHoursPlayed; private set { _totalHoursPlayed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalHoursPlayed))); } }
        public decimal TodayEarnings { get; private set; }
        public decimal ThisWeekEarnings { get; private set; }
        public decimal ThisMonthEarnings { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? ViewAllSessionsRequested;
        public ObservableCollection<SessionItemViewModel> RecentSessions { get; } = new BatchObservableCollection<SessionItemViewModel>();
        public event EventHandler<Game>? ViewGameRequested;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private bool _isLoading;
        private bool _hasLoaded;
        private bool _reloadRequested;
        private bool _queuedSavedRefresh;

        private List<Session> _sessions = [];
        private List<Payout> _payouts = [];
        private Dictionary<int, GameMetadata> _artwork = [];
        private DateTime _snapshotDay;
        public IReadOnlyList<DashboardMetric> Metrics { get; private set; } = [];
        public IReadOnlyList<DashboardDay> Trend { get; private set; } = [];
        public IReadOnlyList<DashboardStatus> Statuses { get; private set; } = [];
        public IReadOnlyList<DashboardGame> TopGames { get; private set; } = [];
        public string RangeLabel { get; private set; } = "";
        public string SessionCountLabel { get; private set; } = "";
        public string VerifiedShare { get; private set; } = "";
        public double VerifiedPercent { get; private set; }
        public string GameAttributionNotice { get; private set; } = "";
        internal decimal PeriodEarned => Trend.Sum(day => day.Earned);
        internal decimal PeriodPending => Trend.Sum(day => day.Pending);
        private static DateTime RecordedDay(Session session) => (session.RecordedDate == default ? session.Date : session.RecordedDate).Date;

        private sealed record PeriodReport(DateTime Start, DateTime End, List<Session> Sessions,
            List<DashboardDay> Days, List<SessionItemViewModel> Recent, List<DashboardGame> Games,
            decimal Paid, double Hours, bool HasEarningsActivity);

        private PeriodReport BuildPeriod(List<Session> sessions, List<Payout> payouts, Dictionary<int, GameMetadata> artwork, DateTime today)
        {
            var start = today.AddDays(1 - new[] { 7, 30, 90 }[Math.Clamp(PeriodSelector.SelectedIndex, 0, 2)]);
            var rows = sessions.Where(session => RecordedDay(session) >= start && RecordedDay(session) <= today).ToList();
            var adjustments = payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment)
                .Where(row => row.Date.Date >= start && row.Date.Date <= today).ToList();
            var byDay = rows.ToLookup(RecordedDay);
            var changesByDay = adjustments.ToLookup(row => row.Date.Date);
            var days = Enumerable.Range(0, (today - start).Days + 1).Select(offset =>
            {
                var date = start.AddDays(offset);
                var daily = byDay[date].ToList();
                // Shared reporting owns verified eligibility and signed separate adjustments.
                var earned = EarningsPeriods.Calculate(daily, changesByDay[date], date).Today;
                var separate = changesByDay[date].Sum(row => row.Amount);
                return new DashboardDay(date, earned - separate, separate,
                    daily.Where(row => row.Status == SessionStatus.Pending).Sum(row => row.Earnings));
            }).ToList();
            var recent = rows.OrderByDescending(session => session.RecordedDate == default ? session.Date : session.RecordedDate)
                .ThenByDescending(session => session.Id).Take(5).Select(session => new SessionItemViewModel(session)).ToList();
            foreach (var item in recent)
                item.SetArtwork(item.Session.GameId is int id ? artwork.GetValueOrDefault(id) : null);
            // Preserve separate rate identities and never invent a link for unlinked history.
            var groups = rows.Where(row => row.Status == SessionStatus.Verified)
                .GroupBy(row => (row.GameId, UnlinkedName: row.GameId.HasValue ? "" : row.GameName))
                .OrderByDescending(group => group.Sum(row => row.Earnings)).ThenBy(group => group.Key.GameId).ThenBy(group => group.Key.UnlinkedName).ToList();
            var maximum = groups.Count == 0 ? 0m : groups.Max(group => group.Sum(row => row.Earnings));
            var games = groups.Take(4).Select(group =>
            {
                var first = group.First();
                var artworkItem = new SessionItemViewModel(first);
                artworkItem.SetArtwork(first.GameId is int id ? artwork.GetValueOrDefault(id) : null);
                var earnings = group.Sum(row => row.Earnings);
                return new DashboardGame(first.Game, first.Game?.Name ?? first.GameName, group.Count(), earnings,
                    maximum > 0 ? (double)(earnings / maximum * 100m) : 0, artworkItem);
            }).ToList();
            var paid = PayoutAccounting.Calculate([], payouts.Where(row => row.Date.Date >= start && row.Date.Date <= today)).TotalPaidOut;
            var hours = rows.Where(row => row.Status is SessionStatus.Verified or SessionStatus.Pending).Sum(row => row.Duration.TotalHours);
            return new(start, today, rows, days, recent, games, paid, hours,
                rows.Any(row => row.Status is SessionStatus.Verified or SessionStatus.Pending) || adjustments.Count > 0);
        }

        private void PublishPeriod(PeriodReport report)
        {
            Trend = report.Days;
            TopGames = report.Games;
            var verified = report.Sessions.Count(row => row.Status == SessionStatus.Verified);
            Brush BrushFor(string key) => (Brush)FindResource(key);
            Statuses = new[] { SessionStatus.Verified, SessionStatus.Pending, SessionStatus.Rejected, SessionStatus.NotUploaded }
                .Select(status => new DashboardStatus(status, report.Sessions.Count(row => row.Status == status),
                    report.Sessions.Where(row => row.Status == status).Sum(row => row.Earnings), BrushFor(status switch
                    { SessionStatus.Verified => "VerifiedTextColor", SessionStatus.Pending => "PendingTextColor", SessionStatus.Rejected => "RejectedTextColor", _ => "NotUploadedTextColor" }))).ToList();
            Metrics = [new("Earned", CurrencyDisplay.FormatUsd(PeriodEarned), "Verified + signed adjustments · period", BrushFor("DefaultTextColor")),
                new("Pending earnings", CurrencyDisplay.FormatUsd(PeriodPending), "Not yet verified · period", BrushFor("PendingTextColor")),
                new("Available balance", BalanceDisplay, "Lifetime posted-date balance", BrushFor("VerifiedTextColor")),
                new("Payouts", CurrencyDisplay.FormatUsd(report.Paid), "Paid + Processing · payment date", BrushFor("DefaultTextColor")),
                new("Hours played", $"{report.Hours:N1} h", "Verified + Pending · recording date", BrushFor("DefaultTextColor"))];
            RangeLabel = $"{report.Start:MMM d} – {report.End:MMM d, yyyy} · inclusive recording dates; adjustments/payment dates are separate";
            SessionCountLabel = $"{report.Sessions.Count:N0} sessions · recording date";
            VerifiedPercent = report.Sessions.Count == 0 ? 0 : verified * 100d / report.Sessions.Count;
            VerifiedShare = report.Sessions.Count == 0 ? "No sessions" : $"{VerifiedPercent:0}% verified";
            GameAttributionNotice = TopGames.Count == 0 ? "No verified session earnings in this period." : "Top four saved game identities. Unlinked history has no navigation target.";
            ((BatchObservableCollection<SessionItemViewModel>)RecentSessions).ReplaceAll(report.Recent);
            DashboardViewEmptyState.Visibility = report.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TrendChart.Visibility = report.HasEarningsActivity ? Visibility.Visible : Visibility.Collapsed;
            TrendEmptyState.Visibility = report.HasEarningsActivity ? Visibility.Collapsed : Visibility.Visible;
            foreach (var property in new[] { nameof(Metrics), nameof(Trend), nameof(TopGames), nameof(Statuses), nameof(RangeLabel), nameof(SessionCountLabel), nameof(VerifiedPercent), nameof(VerifiedShare), nameof(GameAttributionNotice) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        private void Period_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_hasLoaded || _isLoading) return;
            PublishPeriod(BuildPeriod(_sessions, _payouts, _artwork, _snapshotDay));
        }

        private void TopGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DashboardGame { Game: not null } item }) ViewGameRequested?.Invoke(this, item.Game);
        }

        public DashboardView() : this(null) { }

        internal DashboardView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            InitializeComponent();

            DataContext = this;
            Loaded += async (s, e) => { PayoutService.DataChanged += PayoutDataChanged; await LoadDashboardData(); };
            Unloaded += (s, e) => PayoutService.DataChanged -= PayoutDataChanged;
        }

        private void Dashboard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var stacked = ActualWidth - 40 < 850;
            foreach (var panel in new[] { DistributionPanel, GamesPanel })
            {
                Grid.SetColumn(panel, stacked ? 0 : 2);
                Grid.SetRow(panel, stacked ? 1 : 0);
                panel.Margin = stacked ? new Thickness(0, 16, 0, 0) : new Thickness(0);
            }
            Grid.SetColumnSpan(TrendPanel, stacked ? 3 : 1);
            Grid.SetColumnSpan(ActivityPanel, stacked ? 3 : 1);
            Grid.SetColumnSpan(DistributionPanel, stacked ? 3 : 1);
            Grid.SetColumnSpan(GamesPanel, stacked ? 3 : 1);
        }

        private void ViewAllSessions_Click(object sender, RoutedEventArgs e)
        {
            ViewAllSessionsRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void PayoutDataChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => PayoutDataChanged(sender, e)); return; }
            await LoadDashboardData(changesSaved: true);
        }

        public async Task LoadDashboardData(bool changesSaved = false, DateTime? asOfDate = null)
        {
            if (_isLoading)
            {
                // A saved change during a read must trigger another read when this one finishes.
                _reloadRequested = true;
                _queuedSavedRefresh |= changesSaved;
                return;
            }
            do
            {
                _reloadRequested = false;
                _queuedSavedRefresh = false;
                _isLoading = true;
                LoadStatusPanel.Visibility = Visibility.Visible;
                LoadStatusText.Text = "Loading Dashboard…";
                RetryLoadButton.IsEnabled = PeriodSelector.IsEnabled = false;
                try
                {
                    using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                    var allSessions = await db.Sessions.Include(session => session.Game).AsNoTracking().ToListAsync();
                    var payouts = await db.Payouts.ToListAsync();
                    var totals = PayoutAccounting.Calculate(allSessions, payouts);
                    var artwork = await db.GameMetadata.AsNoTracking().ToDictionaryAsync(item => item.GameId);
                    var hours = allSessions.Sum(session => session.Duration.TotalHours);
                    var periods = EarningsPeriods.Calculate(allSessions, payouts, asOfDate ?? DateTime.Today);
                    var today = (asOfDate ?? DateTime.Today).Date;
                    var report = BuildPeriod(allSessions, payouts, artwork, today);
                    // A queued refresh supersedes this read; keep the displayed snapshot until the next succeeds.
                    if (_reloadRequested) { changesSaved |= _queuedSavedRefresh; continue; }

                    // Publish only after every database read and calculation succeeds.
                    TodayEarnings = periods.Today;
                    ThisWeekEarnings = periods.Week;
                    ThisMonthEarnings = periods.Month;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TodayEarnings)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThisWeekEarnings)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThisMonthEarnings)));
                    TotalEarned = totals.TotalEarned;
                    TotalPaidOut = totals.TotalPaidOut;
                    Balance = totals.Balance;
                    var cutoff = payouts.Where(PayoutAccounting.IsCountedAsPaidPayment).Select(row => (DateTime?)row.Date.Date).Max();
                    BalanceDescription = $"Lifetime earned: {CurrencyDisplay.FormatUsd(TotalEarned)} · Paid + Processing: {CurrencyDisplay.FormatUsd(TotalPaidOut)}. Balance uses verified posted dates {(cutoff.HasValue ? $"after {cutoff:MMM d, yyyy}" : "with no payment cutoff")}; independent of the selected period."
                        + (totals.UndatedVerifiedSessions > 0 ? $" Balance excludes {totals.UndatedVerifiedSessions:N0} sessions without posted dates." : "");
                    TotalSessions = allSessions.Count;
                    TotalHoursPlayed = hours;
                    _sessions = allSessions;
                    _payouts = payouts;
                    _artwork = artwork;
                    _snapshotDay = today;
                    PublishPeriod(report);
                    _hasLoaded = true;
                    TotalsPanel.Visibility = RecordingsPanel.Visibility = Visibility.Visible;
                    LoadStatusPanel.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                    LoadStatusText.Text = (changesSaved ? "Changes were saved, but Dashboard could not be refreshed. " : "Could not load Dashboard. ")
                        + (_hasLoaded ? "Previous values are shown and may be out of date. " : "Totals are unavailable. ")
                        + ex.GetBaseException().Message;
                }
                finally
                {
                    _isLoading = false;
                    RetryLoadButton.IsEnabled = PeriodSelector.IsEnabled = true;
                }
                changesSaved = _queuedSavedRefresh;
            } while (_reloadRequested);
        }

        private async void RetryLoadButton_Click(object sender, RoutedEventArgs e) => await LoadDashboardData();

        public void SessionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionsList.SelectedItem is SessionItemViewModel session)
            {
                if (session.Game == null)
                {
                    MessageBox.Show("This session has no linked game. Open Sessions, choose Edit, select the correct game, and save to link it.",
                        "Game unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ViewGameRequested?.Invoke(this, session.Game);
            }
        }
    }
}




