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
            // Only change layout at a breakpoint; no chart/tree rebuild per resize event.
            var columns = e.NewSize.Width >= 600 ? 2 : 1;
            if (MetricCards.Columns != columns) MetricCards.Columns = columns;
            // Keep recent activity in a finite viewport even on short windows.
            TotalsScroll.MaxHeight = Math.Max(120, Math.Min(e.NewSize.Height * 0.40, e.NewSize.Height - 300));
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
                RetryLoadButton.IsEnabled = false;
                try
                {
                    using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                    var allSessions = await db.Sessions.ToListAsync();
                    var payouts = await db.Payouts.ToListAsync();
                    var totals = PayoutAccounting.Calculate(allSessions, payouts);
                    var sessions = await db.Sessions.Include(s => s.Game).OrderByDescending(s => s.Date).Take(5).ToListAsync();
                    var recentSessions = sessions.Select(session => new SessionItemViewModel(session)).ToList();
                    var gameIds = sessions.Where(x => x.GameId.HasValue).Select(x => x.GameId!.Value).Distinct().ToList();
                    var artwork = await db.GameMetadata.AsNoTracking().Where(x => gameIds.Contains(x.GameId)).ToDictionaryAsync(x => x.GameId);
                    foreach (var item in recentSessions)
                        item.SetArtwork(item.Session.GameId is int id ? artwork.GetValueOrDefault(id) : null);
                    var hours = allSessions.Sum(session => session.Duration.TotalHours);
                    var periods = EarningsPeriods.Calculate(allSessions, payouts, asOfDate ?? DateTime.Today);
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
                    BalanceDescription = totals.UndatedVerifiedSessions == 0
                        ? $"Total earned: {TotalEarned.ToString("C2", CultureInfo.GetCultureInfo("en-US"))}"
                        : $"Total earned: {TotalEarned.ToString("C2", CultureInfo.GetCultureInfo("en-US"))} · Balance excludes {totals.UndatedVerifiedSessions:N0} sessions without posted dates";
                    TotalSessions = allSessions.Count;
                    TotalHoursPlayed = hours;
                    ((BatchObservableCollection<SessionItemViewModel>)RecentSessions).ReplaceAll(recentSessions);
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
                    RetryLoadButton.IsEnabled = true;
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




