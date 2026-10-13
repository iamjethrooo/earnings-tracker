using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using System.Windows.Threading;

namespace nerve_earnings_tracker.Views
{
    public partial class StatisticsView : UserControl, INotifyPropertyChanged
    {
        internal enum EarningsBucket { Day, Week, Month }
        private DateTime _rangeStart;
        private DateTime _rangeEnd;
        private EarningsBucket _bucket;
        private List<EarningsPeriodItem> _trendPeriods = [];
        public IReadOnlyList<EarningsPeriodItem> TrendPeriods => _trendPeriods;
        private DispatcherOperation? _trendRender;
        public event PropertyChangedEventHandler? PropertyChanged;
        private List<Session> _sessions = [];
        private List<Payout> _payouts = [];
        private DateTime _snapshotDay;
        private bool _hasRangeData;
        private int _trendVersion;
        private (double Width, double Height, int Version)? _lastTrendRender;
        public string RangeDescription => $"{_rangeStart:MMM d, yyyy} – {_rangeEnd:MMM d, yyyy} · {_bucket.ToString().ToLowerInvariant()} buckets · USD";
        private string? _capSortProperty;
        private ListSortDirection _capSortDirection = ListSortDirection.Ascending;
        private GridViewColumnHeader? _capSortHeader;

        public decimal TodayEarnings { get; private set; }
        public decimal ThisWeekEarnings { get; private set; }
        public decimal ThisMonthEarnings { get; private set; }
        public int UnlinkedSessionCount { get; private set; }
        public string GameAssociationNotice => UnlinkedSessionCount == 0 ? string.Empty
            : $"{UnlinkedSessionCount:N0} {(UnlinkedSessionCount == 1 ? "recording has" : "recordings have")} no linked game and {(UnlinkedSessionCount == 1 ? "is" : "are")} excluded from cap utilization. Verified earnings remain in totals. To link a recording, use the Ambiguous filter in Sessions, edit it, and select a game.";
        public string TodayEarningsDisplay => FormatUsd(TodayEarnings);
        public string ThisWeekEarningsDisplay => FormatUsd(ThisWeekEarnings);
        public string ThisMonthEarningsDisplay => FormatUsd(ThisMonthEarnings);
        public ObservableCollection<GameCapItem> GameCapRows { get; } = new BatchObservableCollection<GameCapItem>();
        public ObservableCollection<GameEarningsItem> GameEarnings { get; } = new BatchObservableCollection<GameEarningsItem>();

        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private bool _isLoading;
        private bool _hasLoaded;
        private bool _reloadRequested;
        private bool _queuedSavedRefresh;

        public StatisticsView() : this(null) { }

        internal StatisticsView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            InitializeComponent();
            DataContext = this;
            Loaded += StatisticsView_Loaded;
            Unloaded += StatisticsView_Unloaded;
        }

        private async void StatisticsView_Loaded(object sender, RoutedEventArgs e)
        {
            PayoutService.DataChanged += PayoutDataChanged;
            await LoadStatistics();
        }

        private void StatisticsView_Unloaded(object sender, RoutedEventArgs e)
        {
            PayoutService.DataChanged -= PayoutDataChanged;
            _trendRender?.Abort(); _trendRender = null;
            _lastTrendRender = null;
        }

        private async void PayoutDataChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => PayoutDataChanged(sender, e)); return; }
            await LoadStatistics(changesSaved: true);
        }

        private void EarningsRangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_hasLoaded || _isLoading) return;
            SetEarningsRange(_sessions, _payouts, _snapshotDay);
            RefreshBindings();
            RequestTrendRender();
        }

        private void MetricCards_SizeChanged(object sender, SizeChangedEventArgs e) =>
            MetricCards.Columns = e.NewSize.Width < 640 ? 1 : 3;

        internal async Task LoadStatistics(bool changesSaved = false, DateTime? asOfDate = null)
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
                LoadStatusText.Text = "Loading Statistics…";
                RetryLoadButton.IsEnabled = EarningsRangeComboBox.IsEnabled = false;
                try
                {
                    using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                    var sessions = await db.Sessions.Where(session => session.Status == SessionStatus.Verified).ToListAsync();
                    var recordedSessionGameIds = await db.Sessions.Select(session => session.GameId).ToListAsync();
                    var payouts = await db.Payouts.ToListAsync();
                    var games = await db.Games.Where(game => !game.IsArchived).OrderBy(game => game.Name).ToListAsync();

                    var today = (asOfDate ?? DateTime.Today).Date;
                    var periods = EarningsPeriods.Calculate(sessions, payouts, today);
                    TodayEarnings = periods.Today;
                    ThisWeekEarnings = periods.Week;
                    ThisMonthEarnings = periods.Month;
                    var recordedGameIds = recordedSessionGameIds.Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
                    UnlinkedSessionCount = recordedSessionGameIds.Count(id => !id.HasValue);
                    UpdateGameCapRows(games, sessions, recordedGameIds);
                    SetEarningsRange(sessions, payouts, today);
                    _sessions = sessions;
                    _payouts = payouts;
                    _snapshotDay = today;
                    RefreshBindings();
                    RequestTrendRender();
                    _hasLoaded = true;
                    TotalsPanel.Visibility = Visibility.Visible;
                    LoadStatusPanel.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceError(ex.ToString());
                    LoadStatusText.Text = (changesSaved ? "Changes were saved, but Statistics could not be refreshed. " : "Could not load Statistics. ")
                        + (_hasLoaded ? "Previous values and charts are shown and may be out of date. " : "Totals are unavailable. ")
                        + ex.GetBaseException().Message;
                }
                finally
                {
                    _isLoading = false;
                    RetryLoadButton.IsEnabled = EarningsRangeComboBox.IsEnabled = true;
                }
                changesSaved = _queuedSavedRefresh;
            } while (_reloadRequested);
        }

        private async void RetryLoadButton_Click(object sender, RoutedEventArgs e) => await LoadStatistics();

        private void UpdateGameCapRows(List<Game> games, List<Session> sessions, HashSet<int> recordedGameIds)
        {
            var rows = new List<GameCapItem>();
            var sessionsByGame = sessions.ToLookup(session => session.GameId);
            foreach (var game in games)
            {
                if (!recordedGameIds.Contains(game.Id))
                    continue;

                // Historical names can refer to multiple cap/rate versions. Only an explicit link establishes attribution.
                IEnumerable<Session> gameSessions = sessionsByGame[game.Id];
                if (game.CapResetDate.HasValue)
                    gameSessions = gameSessions.Where(session => GetRecordedDay(session) >= game.CapResetDate.Value.Date);

                var matchingSessions = gameSessions.ToList();
                var verifiedMinutes = matchingSessions.Sum(session => (decimal)session.Duration.TotalMinutes);
                rows.Add(new GameCapItem(game, verifiedMinutes, matchingSessions.Sum(session => session.Earnings)));
            }
            ((BatchObservableCollection<GameCapItem>)GameCapRows).ReplaceAll(rows);
        }

        private void SetEarningsRange(List<Session> sessions, List<Payout> payouts, DateTime today)
        {
            var selectedRange = (EarningsRangeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "30";
            PayoutRangeNotice.Visibility = Visibility.Collapsed;
            switch (selectedRange)
            {
                case "Payout":
                    var latestPaymentDate = payouts.Where(PayoutAccounting.IsCountedAsPaidPayment)
                        .Select(payout => (DateTime?)payout.Date.Date).Max();
                    var undatedSessionCount = sessions.Count(session => !session.PostedDate.HasValue);
                    // Allocate by posted date, but chart by recording date so later verification
                    // does not move a recording's earnings into a different recording day.
                    sessions = sessions.Where(session => session.PostedDate.HasValue
                        && session.PostedDate.Value.Date <= today
                        && (!latestPaymentDate.HasValue || session.PostedDate.Value.Date > latestPaymentDate.Value)).ToList();
                    payouts = payouts.Where(payout => !latestPaymentDate.HasValue
                        || payout.Date.Date > latestPaymentDate.Value).ToList();
                    _rangeStart = sessions.Select(GetRecordedDay)
                        .Concat(payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment).Select(payout => payout.Date.Date))
                        .Where(date => date <= today).DefaultIfEmpty(today).Min();
                    var rangeDays = (today - _rangeStart).TotalDays;
                    if (rangeDays <= 90)
                        _bucket = EarningsBucket.Day;
                    else if (rangeDays <= 365)
                        _bucket = EarningsBucket.Week;
                    else
                        _bucket = EarningsBucket.Month;
                    PayoutRangeNotice.Text = latestPaymentDate.HasValue
                        ? $"Current payout: earnings posted after {latestPaymentDate.Value:MMM d, yyyy}. Charts use recording dates."
                        : "Current payout: no Paid or Processing payment yet. Charts use recording dates.";
                    if (undatedSessionCount > 0)
                        PayoutRangeNotice.Text += $" {undatedSessionCount:N0} verified session(s) with no posted date are excluded.";
                    PayoutRangeNotice.Visibility = Visibility.Visible;
                    break;
                case "90":
                    _rangeStart = today.AddDays(-89);
                    _bucket = EarningsBucket.Week;
                    break;
                case "Year":
                    _rangeStart = new DateTime(today.Year, 1, 1);
                    _bucket = EarningsBucket.Month;
                    break;
                case "All":
                    var sessionDates = sessions.Select(GetRecordedDay);
                    var adjustmentDates = payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment).Select(payout => payout.Date.Date);
                    var firstDate = sessionDates.Concat(adjustmentDates).DefaultIfEmpty(today).Min();
                    _rangeStart = new DateTime(firstDate.Year, firstDate.Month, 1);
                    _bucket = EarningsBucket.Month;
                    break;
                default:
                    _rangeStart = today.AddDays(-29);
                    _bucket = EarningsBucket.Day;
                    break;
            }
            _rangeEnd = today;

            _hasRangeData = false;
            var periodTotals = new Dictionary<DateTime, EarningsPeriodItem>();
            for (var date = GetBucketStart(_rangeStart, _bucket); date <= GetBucketStart(_rangeEnd, _bucket); date = GetNextBucket(date, _bucket))
                periodTotals[date] = new EarningsPeriodItem(date, _bucket);

            foreach (var session in sessions)
            {
                var sessionDate = GetRecordedDay(session);
                if (sessionDate < _rangeStart || sessionDate > _rangeEnd)
                    continue;
                _hasRangeData = true;
                periodTotals[GetBucketStart(sessionDate, _bucket)].SessionEarnings += session.Earnings;
                periodTotals[GetBucketStart(sessionDate, _bucket)].SessionHours += (decimal)session.Duration.TotalHours;
            }

            foreach (var payout in payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment))
            {
                var adjustmentDate = payout.Date.Date;
                if (adjustmentDate < _rangeStart || adjustmentDate > _rangeEnd)
                    continue;
                _hasRangeData = true;
                periodTotals[GetBucketStart(adjustmentDate, _bucket)].AdjustmentEarnings += payout.Amount;
            }

            _trendPeriods = periodTotals.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
            _trendVersion++;
            EarningsTrendCanvas.Height = _hasRangeData ? 230 : 80;
            TrendPanel.MinHeight = _hasRangeData ? 300 : 180;
            TrendValuesExpander.Visibility = _hasRangeData ? Visibility.Visible : Visibility.Collapsed;
            var gameTotals = sessions
                .Where(session =>
                {
                    var sessionDate = GetRecordedDay(session);
                    return sessionDate >= _rangeStart && sessionDate <= _rangeEnd;
                })
                .GroupBy(session => session.GameName)
                .Select(group => new { Name = group.Key, Earnings = group.Sum(session => session.Earnings) })
                .OrderByDescending(group => group.Earnings)
                .Take(8)
                .ToList();
            var maxEarnings = gameTotals.Count == 0 ? 0m : gameTotals.Max(group => Math.Max(0m, group.Earnings));

            ((BatchObservableCollection<GameEarningsItem>)GameEarnings).ReplaceAll(gameTotals.Select(total => new GameEarningsItem
                {
                    GameName = total.Name,
                    Earnings = total.Earnings,
                    EarningsText = FormatUsd(total.Earnings),
                    Share = maxEarnings > 0 ? (double)Math.Max(0m, total.Earnings) / (double)maxEarnings * 100d : 0d
                }));
            GameEarningsPanel.MinHeight = GameEarnings.Count > 0 ? 300 : 180;
        }

        private void GameCapList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (GameCapList.View is not GridView view || view.Columns.Count != 7 || GameCapList.ActualWidth <= 0)
                return;

            // Reserve room for row padding and the vertical scrollbar even when it appears later.
            var availableWidth = Math.Max(0, GameCapList.ActualWidth - 32);
            var proportions = new[] { 0.23, 0.15, 0.09, 0.14, 0.19, 0.09, 0.11 };
            for (var i = 0; i < view.Columns.Count; i++)
                view.Columns[i].Width = availableWidth * proportions[i];
        }

        private void ChartsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var stacked = e.NewSize.Width < 820;
            ChartsGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(0.85, GridUnitType.Star);
            Grid.SetColumn(GameEarningsPanel, stacked ? 0 : 1);
            Grid.SetRow(GameEarningsPanel, stacked ? 1 : 0);
            TrendPanel.Margin = stacked ? new Thickness(0, 0, 0, 14) : new Thickness(0, 0, 7, 0);
            GameEarningsPanel.Margin = stacked ? new Thickness(0) : new Thickness(7, 0, 0, 0);
        }

        private void GameCapList_ColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Content is not string clickedHeader)
                return;

            var label = clickedHeader.TrimEnd(' ', '\u25B2', '\u25BC');
            var property = label switch
            {
                "GAME" => nameof(GameCapItem.GameName),
                "VERIFIED TIME" => nameof(GameCapItem.VerifiedMinutes),
                "CAP" => nameof(GameCapItem.MinutesCap),
                "CYCLE START" => nameof(GameCapItem.CycleStartDate),
                "UTILIZATION" => nameof(GameCapItem.Utilization),
                "RATE" => nameof(GameCapItem.Rate),
                "EARNED" => nameof(GameCapItem.Earnings),
                _ => null
            };
            if (property == null)
                return;

            _capSortDirection = _capSortProperty == property && _capSortDirection == ListSortDirection.Descending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            _capSortProperty = property;

            var view = CollectionViewSource.GetDefaultView(GameCapList.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, _capSortDirection));

            if (_capSortHeader != null && _capSortHeader != header)
                _capSortHeader.Content = _capSortHeader.Content?.ToString()?.TrimEnd(' ', '\u25B2', '\u25BC');
            header.Content = $"{label} {(_capSortDirection == ListSortDirection.Descending ? "\u25BC" : "\u25B2")}";
            _capSortHeader = header;
        }

        private void EarningsTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RequestTrendRender();

        private void RequestTrendRender()
        {
            if (_trendRender?.Status == DispatcherOperationStatus.Pending) return;
            _trendRender = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _trendRender = null;
                DrawEarningsTrend();
            }));
        }

        private void DrawEarningsTrend()
        {
            var width = EarningsTrendCanvas.ActualWidth;
            var height = EarningsTrendCanvas.ActualHeight;
            if (width < 100 || height < 80)
                return;
            var renderKey = (width, height, _trendVersion);
            if (_lastTrendRender == renderKey) return;
            _lastTrendRender = renderKey;
            EarningsTrendCanvas.Children.Clear();
            var chartGridBrush = (Brush)FindResource("DefaultBorderColor");
            var chartLabelBrush = (Brush)FindResource("SecondaryTextColor");
            var chartLineBrush = (Brush)FindResource("AccentColor");
            if (!_hasRangeData)
            {
                AddCanvasText("No verified sessions or separate adjustments in this range.", 8, 8, chartLabelBrush, width - 16);
                return;
            }

            var minValue = Math.Min(0m, _trendPeriods.Min(period => period.TotalEarnings));
            var maxValue = Math.Max(0m, _trendPeriods.Max(period => period.TotalEarnings));
            if (minValue == maxValue)
                maxValue = minValue + 1m;
            // Measure actual USD labels so large totals and signed corrections remain readable.
            var tickLabels = Enumerable.Range(0, 5).Select(i => FormatUsd(maxValue - (maxValue - minValue) * i / 4m));
            var left = tickLabels.Select(text =>
            {
                var label = new TextBlock { Text = text, FontSize = (double)FindResource("FontSizeSupporting"), FontFamily = FontFamily };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return label.DesiredSize.Width;
            }).Max() + 12;
            const double right = 8;
            const double top = 10;
            const double bottom = 30;
            var plotWidth = Math.Max(1, width - left - right);
            var plotHeight = Math.Max(1, height - top - bottom);

            double GetY(decimal value) => top + (double)(maxValue - value) / (double)(maxValue - minValue) * plotHeight;

            for (var i = 0; i <= 4; i++)
            {
                var value = maxValue - (maxValue - minValue) * i / 4m;
                var y = GetY(value);
                EarningsTrendCanvas.Children.Add(new Line
                {
                    X1 = left,
                    X2 = width - right,
                    Y1 = y,
                    Y2 = y,
                    Stroke = chartGridBrush,
                    StrokeThickness = 1
                });
                AddCanvasText(FormatUsd(value), 0, y - 8, chartLabelBrush, left - 10, double.NaN, TextAlignment.Right);
            }

            var points = new PointCollection();
            for (var i = 0; i < _trendPeriods.Count; i++)
            {
                var x = _trendPeriods.Count == 1
                    ? left + plotWidth / 2
                    : left + plotWidth * i / (_trendPeriods.Count - 1);
                var period = _trendPeriods[i];
                var y = GetY(period.TotalEarnings);
                points.Add(new Point(x, y));

                var marker = new Ellipse
                {
                    Width = 18,
                    Height = 18,
                    Fill = Brushes.Transparent,
                    StrokeThickness = 0,
                    ToolTip = new ToolTip
                    {
                        Style = (Style)FindResource("ChartToolTip"),
                        Content = new TextBlock
                        {
                            Text = $"{period.TooltipDate}\nTotal earnings: {period.EarningsText}\nSessions: {FormatUsd(period.SessionEarnings)}\nSeparate adjustments: {FormatUsd(period.AdjustmentEarnings)}\nHours: {period.SessionHours:N2}",
                            Foreground = (Brush)FindResource("DefaultTextColor"),
                            FontSize = (double)FindResource("FontSizeSupporting")
                        }
                    }
                };
                ToolTipService.SetInitialShowDelay(marker, 100);
                ToolTipService.SetShowDuration(marker, 30000);
                Canvas.SetLeft(marker, x - marker.Width / 2);
                Canvas.SetTop(marker, y - marker.Height / 2);
                EarningsTrendCanvas.Children.Add(marker);

                var visibleDot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = chartLineBrush,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(visibleDot, x - visibleDot.Width / 2);
                Canvas.SetTop(visibleDot, y - visibleDot.Height / 2);
                EarningsTrendCanvas.Children.Add(visibleDot);
            }

            var line = new Polyline
            {
                Points = points,
                Stroke = chartLineBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            EarningsTrendCanvas.Children.Insert(5, line);

            var labelInterval = Math.Max(1, (int)Math.Ceiling(_trendPeriods.Count / Math.Max(1, Math.Floor(plotWidth / 76))));
            for (var i = 0; i < _trendPeriods.Count; i += labelInterval)
            {
                var x = _trendPeriods.Count == 1 ? left + plotWidth / 2 : left + plotWidth * i / (_trendPeriods.Count - 1);
                AddCanvasText(_trendPeriods[i].Label, Math.Clamp(x - 36, left, Math.Max(left, width - 72)), height - bottom + 8, chartLabelBrush, 72, double.NaN, TextAlignment.Center);
            }
        }

        private void AddCanvasText(string text, double x, double y, Brush foreground, double width = double.NaN, double height = double.NaN, TextAlignment alignment = TextAlignment.Left)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = (double)FindResource("FontSizeSupporting"),
                TextWrapping = TextWrapping.Wrap,
                Width = width,
                Height = height,
                TextAlignment = alignment,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            EarningsTrendCanvas.Children.Add(label);
        }

        // Date-only source values must remain on their recorded day; PostedDate is a separate verification date.
        private static DateTime GetRecordedDay(Session session) =>
            (session.RecordedDate == default ? session.Date : session.RecordedDate).Date;

        private static DateTime GetBucketStart(DateTime date, EarningsBucket bucket) => bucket switch
        {
            EarningsBucket.Week => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            EarningsBucket.Month => new DateTime(date.Year, date.Month, 1),
            _ => date.Date
        };

        private static DateTime GetNextBucket(DateTime date, EarningsBucket bucket) => bucket switch
        {
            EarningsBucket.Week => date.AddDays(7),
            EarningsBucket.Month => date.AddMonths(1),
            _ => date.AddDays(1)
        };

        private static string FormatUsd(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));

        private void RefreshBindings()
        {
            foreach (var property in new[] { nameof(TodayEarningsDisplay), nameof(ThisWeekEarningsDisplay),
                nameof(ThisMonthEarningsDisplay), nameof(GameAssociationNotice), nameof(UnlinkedSessionCount),
                nameof(TrendPeriods), nameof(RangeDescription) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public sealed class GameCapItem
    {
        public string GameName { get; }
        public decimal MinutesCap { get; }
        public decimal VerifiedMinutes { get; }
        public decimal Earnings { get; }
        public decimal Rate { get; }
        public double Utilization { get; }
        public double ProgressValue => Math.Clamp(Utilization, 0d, 100d);
        public string VerifiedTimeText => FormatMinutes(VerifiedMinutes);
        public string CapText => MinutesCap > 0 ? FormatMinutes(MinutesCap) : "No cap";
        public string CycleStartText { get; }
        public DateTime CycleStartDate { get; }
        public string UtilizationText => MinutesCap > 0 ? $"{Utilization:N0}%" : "—";
        public string RateText { get; }
        public string EarningsText { get; }

        public GameCapItem(Game game, decimal verifiedMinutes, decimal earnings)
        {
            GameName = game.Name;
            MinutesCap = game.MinutesCap;
            VerifiedMinutes = verifiedMinutes;
            Earnings = earnings;
            Rate = game.RatePerHour;
            Utilization = MinutesCap > 0 ? (double)(VerifiedMinutes / MinutesCap * 100m) : 0d;
            CycleStartDate = game.CapResetDate?.Date ?? DateTime.MinValue;
            CycleStartText = game.CapResetDate?.ToString("MMM d, yyyy") ?? "All history";
            RateText = game.RatePerHour.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
            EarningsText = earnings.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        }

        private static string FormatMinutes(decimal minutes)
        {
            var totalMinutes = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
            var hours = totalMinutes / 60;
            var remaining = totalMinutes % 60;
            return hours > 0 ? $"{hours}h {remaining}m" : $"{remaining}m";
        }
    }

    public sealed class EarningsPeriodItem
    {
        public DateTime StartDate { get; }
        public decimal SessionEarnings { get; set; }
        public decimal AdjustmentEarnings { get; set; }
        public decimal SessionHours { get; set; }
        public decimal TotalEarnings => SessionEarnings + AdjustmentEarnings;
        public string Label { get; }
        public string TooltipDate { get; }
        public string EarningsText => TotalEarnings.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        public string HoursText => $"{SessionHours:N2} h";
        public string AccessibleSummary => $"{TooltipDate}. Total earnings: {EarningsText}. Hours: {SessionHours:N2}.";

        internal EarningsPeriodItem(DateTime startDate, StatisticsView.EarningsBucket bucket)
        {
            StartDate = startDate;
            if (bucket == StatisticsView.EarningsBucket.Day)
            {
                Label = startDate.ToString("M/d");
                TooltipDate = startDate.ToString("MMMM d, yyyy");
            }
            else if (bucket == StatisticsView.EarningsBucket.Week)
            {
                Label = startDate.ToString("M/d");
                TooltipDate = $"Week of {startDate:MMMM d, yyyy}";
            }
            else
            {
                Label = startDate.ToString("MMM yy");
                TooltipDate = startDate.ToString("MMMM yyyy");
            }
        }
    }

    public sealed class GameEarningsItem
    {
        public string GameName { get; init; } = string.Empty;
        public decimal Earnings { get; init; }
        public string EarningsText { get; init; } = string.Empty;
        public double Share { get; init; }
        public string AccessibleSummary => $"{GameName}. Verified session earnings: {EarningsText}. {Share:N0}% of the highest earner in this range.";
    }
}

