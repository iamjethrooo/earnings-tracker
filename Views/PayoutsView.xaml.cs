using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using System.Windows.Data;

namespace EarningsTracker.Views
{
    public partial class PayoutsView : UserControl
    {
        public event EventHandler? ImportPayoutsRequested;
        public event EventHandler? AddPayoutRequested;
        public event EventHandler<Payout>? EditPayoutRequested;
        public ObservableCollection<PayoutHistoryItem> History { get; } = new BatchObservableCollection<PayoutHistoryItem>();
        private string? _sortProperty;
        private ListSortDirection _sortDirection = ListSortDirection.Descending;
        private GridViewColumnHeader? _sortHeader;

        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private bool _hasLoaded;
        private Task? _refreshTask;
        private bool _reloadRequested, _queuedSavedRefresh;
        internal Task RefreshCompletion => _refreshTask ?? Task.CompletedTask;

        public PayoutsView() : this(null) { }

        internal PayoutsView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            InitializeComponent();
            DataContext = this;
            Loaded += async (s, e) => { PayoutService.DataChanged += PayoutDataChanged; await RefreshHistory(); };
            Unloaded += (s, e) => PayoutService.DataChanged -= PayoutDataChanged;
        }

        private async void PayoutDataChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => PayoutDataChanged(sender, e)); return; }
            await RefreshHistory(changesSaved: true);
        }

        private void PayoutList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PayoutList.View is not GridView view || view.Columns.Count != 6 || PayoutList.ActualWidth <= 0)
                return;

            // Keep dates and amounts readable; narrower viewports can scroll horizontally.
            var minimumWidths = new[] { 100d, 100, 130, 200, 110, 125 };
            // Reserve the vertical scrollbar plus the shared row/presenter insets.
            var extraWidth = Math.Max(0, PayoutList.ActualWidth - 40 - minimumWidths.Sum());
            var proportions = new[] { 0.11, 0.11, 0.13, 0.39, 0.12, 0.14 };
            for (var i = 0; i < view.Columns.Count; i++)
                view.Columns[i].Width = minimumWidths[i] + extraWidth * proportions[i];
        }

        internal Task RefreshHistory(bool changesSaved = false)
        {
            _reloadRequested = true;
            _queuedSavedRefresh |= changesSaved;
            return _refreshTask ??= RefreshHistoryCore();
        }

        private async Task RefreshHistoryCore()
        {
            await Task.Yield();
            LoadStatusPanel.Visibility = Visibility.Visible;
            LoadStatusText.Text = "Loading Payments…";
            RetryLoadButton.IsEnabled = false;
            try
            {
                do
                {
                    _reloadRequested = false;
                    var changesSaved = _queuedSavedRefresh;
                    _queuedSavedRefresh = false;
                    try
                    {
                        var snapshot = await Task.Run(() =>
                        {
                            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                            var items = new List<PayoutHistoryItem>();
                            var payouts = db.Payouts.AsNoTracking().OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToList();
                            var sessions = db.Sessions.AsNoTracking().ToList();
                            var totals = PayoutAccounting.Calculate(sessions, payouts);

                            foreach (var payout in payouts)
                                items.Add(PayoutHistoryItem.FromPayout(payout));

                            foreach (var session in sessions.Where(s => s.Status == SessionStatus.Verified)
                                     .OrderByDescending(s => s.PostedDate ?? s.RecordedDate).ThenByDescending(s => s.Id))
                                items.Add(PayoutHistoryItem.FromSession(session));

                            var history = items.OrderByDescending(item => item.PostedDate ?? item.RecordedDate).ThenByDescending(item => item.SortId).ToList();
                            var unresolvedAdjustments = items.Count(item => item.NeedsReconciliation);
                            return (history, totals, unresolvedAdjustments);
                        });
                        if (_reloadRequested) { _queuedSavedRefresh |= changesSaved; continue; }
                        var unresolvedAdjustments = snapshot.unresolvedAdjustments;
                        TotalEarnedValue.Text = snapshot.totals.TotalEarned.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
                        TotalPaidValue.Text = snapshot.totals.TotalPaidOut.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
                        AmountOwedValue.Text = snapshot.totals.Balance.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
                        ((BatchObservableCollection<PayoutHistoryItem>)History).ReplaceAll(snapshot.history);
                        ReconciliationNotice.Text = $"{unresolvedAdjustments:N0} {(unresolvedAdjustments == 1 ? "adjustment needs" : "adjustments need")} reconciliation and {(unresolvedAdjustments == 1 ? "is" : "are")} excluded from Total Earned. Select a row and choose Edit selected.";
                        ReconciliationNotice.Visibility = unresolvedAdjustments == 0 ? Visibility.Collapsed : Visibility.Visible;
                        var undated = snapshot.totals.UndatedVerifiedSessions;
                        UndatedSessionsNotice.Text = $"{undated:N0} verified {(undated == 1 ? "session has" : "sessions have")} no posted date. Included in Total Earned, excluded from Amount Owed.";
                        UndatedSessionsNotice.Visibility = undated == 0 ? Visibility.Collapsed : Visibility.Visible;
                        _hasLoaded = true;
                        TotalsPanel.Visibility = HistoryPanel.Visibility = Visibility.Visible;
                        LoadStatusPanel.Visibility = Visibility.Collapsed;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError(ex.ToString());
                        LoadStatusText.Text = (changesSaved ? "Changes were saved, but Payments could not be refreshed. " : "Could not load Payments. ")
                            + (_hasLoaded ? "Previous values and transactions are shown and may be out of date. " : "Totals are unavailable. ")
                            + ex.GetBaseException().Message;
                    }
                } while (_reloadRequested);
            }
            finally { _refreshTask = null; RetryLoadButton.IsEnabled = true; }
        }

        private async void RetryLoadButton_Click(object sender, RoutedEventArgs e) => await RefreshHistory();

        private void PayoutList_ColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Content is not string clickedHeader)
                return;

            var label = clickedHeader.TrimEnd(' ', '▲', '▼');
            var property = label switch
            {
                "POSTED" => nameof(PayoutHistoryItem.PostedDate),
                "RECORDED" => nameof(PayoutHistoryItem.RecordedDate),
                "TYPE" => nameof(PayoutHistoryItem.Type),
                "DESCRIPTION" => nameof(PayoutHistoryItem.Description),
                "STATUS" => nameof(PayoutHistoryItem.Status),
                "AMOUNT" => nameof(PayoutHistoryItem.Amount),
                _ => null
            };

            if (property == null)
                return;

            _sortDirection = _sortProperty == property && _sortDirection == ListSortDirection.Descending
                ? ListSortDirection.Ascending
                : ListSortDirection.Descending;
            _sortProperty = property;

            var view = CollectionViewSource.GetDefaultView(PayoutList.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, _sortDirection));

            if (_sortHeader != null && _sortHeader != header)
                _sortHeader.Content = _sortHeader.Content?.ToString()?.TrimEnd(' ', '▲', '▼');
            header.Content = $"{label} {(_sortDirection == ListSortDirection.Descending ? "▼" : "▲")}";
            _sortHeader = header;
        }

        private void PayoutList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PayoutList.SelectedItem is not PayoutHistoryItem item || item.Payout is null)
            {
                EditButton.IsEnabled = DeleteButton.IsEnabled = false;
                return;
            }

            EditButton.IsEnabled = true;
            var payout = item.Payout;
            DeleteButton.IsEnabled = payout.Status != "Legacy" && payout.ImportedAt == null && payout.SourceRow == null && payout.SourceValuesJson == null;
        }

        private void ImportPayoutsButton_Click(object sender, RoutedEventArgs e) => ImportPayoutsRequested?.Invoke(this, EventArgs.Empty);
        private void AddPayoutButton_Click(object sender, RoutedEventArgs e) => AddPayoutRequested?.Invoke(this, EventArgs.Empty);

        private void EditPayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (PayoutList.SelectedItem is PayoutHistoryItem item && item.Payout is not null)
                EditPayoutRequested?.Invoke(this, item.Payout);
        }

        private void DeletePayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (PayoutList.SelectedItem is not PayoutHistoryItem item || item.Payout is not Payout payout || !DeleteButton.IsEnabled)
                return;
            var confirm = MessageBox.Show($"Hide the manual record '{payout.Description}'? It will be hidden from totals but retained in history.", "Hide payout", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
            try
            {
                PayoutService.SoftDeleteManual(payout.Id, "Hidden by user");
            }
            catch (PostCommitRefreshException ex)
            {
                MessageBox.Show(ex.Message, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to hide payout", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public sealed class PayoutHistoryItem
    {
        public Payout? Payout { get; private set; }
        public DateTime? PostedDate { get; private set; }
        public DateTime RecordedDate { get; private set; }
        public int SortId { get; private set; }
        public string PostedText => PostedDate?.ToString("M/d/yyyy") ?? "—";
        public string RecordedText => RecordedDate == default ? "—" : RecordedDate.ToString("M/d/yyyy");
        public string Type { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string Status { get; private set; } = string.Empty;
        public string AmountText { get; private set; } = string.Empty;
        public decimal Amount { get; private set; }
        public string ReconciliationText { get; private set; } = string.Empty;
        public string ReconciliationExplanation { get; private set; } = string.Empty;
        public bool NeedsReconciliation { get; private set; }
        public Brush ReconciliationForeground { get; private set; } = Brushes.White;
        public Brush ReconciliationBackground { get; private set; } = Brushes.Transparent;
        public Brush TypeForeground { get; private set; } = Brushes.White;
        public Brush TypeBackground { get; private set; } = Brushes.Transparent;
        public Brush StatusForeground { get; private set; } = Brushes.White;
        public Brush StatusBackground { get; private set; } = Brushes.Transparent;
        public Brush AmountForeground { get; private set; } = Brushes.White;

        public static PayoutHistoryItem FromPayout(Payout payout)
        {
            var item = new PayoutHistoryItem
            {
                Payout = payout,
                PostedDate = payout.Date,
                RecordedDate = default,
                SortId = payout.Id,
                Type = payout.Type,
                Description = payout.Description,
                Status = payout.Status,
                Amount = payout.Amount,
                AmountText = FormatAmount(payout.Amount, payout.Type),
                AmountForeground = payout.Type.Equals("Payment", StringComparison.OrdinalIgnoreCase)
                    ? payout.Amount < 0 ? Brush(PresentationPalette.PaymentText) : Brush(PresentationPalette.NeutralText)
                    : payout.Amount < 0 ? Brush(PresentationPalette.ErrorText) : Brush(PresentationPalette.SuccessText)
            };
            item.SetTypeColors();
            item.SetStatusColors();
            item.SetReconciliation(payout);
            return item;
        }

        public static PayoutHistoryItem FromSession(Session session)
        {
            var item = new PayoutHistoryItem
            {
                PostedDate = session.PostedDate,
                RecordedDate = session.RecordedDate,
                SortId = session.Id,
                Type = "Earning",
                Description = $"{session.GameName} — {session.DurationText} recording",
                Status = "Verified",
                Amount = session.Earnings,
                AmountText = FormatAmount(session.Earnings, "Earning"),
                TypeForeground = Brush(PresentationPalette.SuccessText),
                TypeBackground = Brush(PresentationPalette.SuccessBackground),
                StatusForeground = Brush(PresentationPalette.SuccessText),
                StatusBackground = Brush(PresentationPalette.SuccessBackground),
                AmountForeground = Brush(PresentationPalette.SuccessText)
            };
            return item;
        }

        private void SetReconciliation(Payout payout)
        {
            if (!Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase)
                && !Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase))
                return;

            ReconciliationForeground = Brush(PresentationPalette.SuccessText);
            ReconciliationBackground = Brush(PresentationPalette.SuccessBackground);
            if (payout.ReconciliationState == "IncludedInSessions" && payout.IsAdjustmentIncludedInSessionEarnings)
            {
                ReconciliationText = "Included in sessions";
                ReconciliationExplanation = "Already reflected in session earnings. This adjustment is not added to Total Earned again.";
            }
            else if (payout.ReconciliationState == "Separate" && !payout.IsAdjustmentIncludedInSessionEarnings)
            {
                if (PayoutAccounting.IsSeparateEarnedAdjustment(payout))
                {
                    ReconciliationText = "Counted separately";
                    ReconciliationExplanation = "The original signed amount is added to Total Earned separately from session earnings. Negative corrections reduce earnings.";
                }
                else
                {
                    ReconciliationText = "Separate · excluded";
                    ReconciliationExplanation = "Reconciled as separate, but excluded from Total Earned because its status is not Verified, Paid, or Processing.";
                    ReconciliationForeground = Brush(PresentationPalette.NeutralText);
                    ReconciliationBackground = Brush(PresentationPalette.NeutralBackground);
                }
            }
            else
            {
                NeedsReconciliation = true;
                ReconciliationText = "Needs reconciliation";
                ReconciliationExplanation = "Excluded from Total Earned until you explicitly mark it as separate from, or already included in, session earnings. Choose Edit selected to reconcile it.";
                ReconciliationForeground = Brush(PresentationPalette.WarningText);
                ReconciliationBackground = Brush(PresentationPalette.WarningBackground);
            }
        }

        private void SetTypeColors()
        {
            var adjustment = Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase) || Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase);
            var payment = Type.Equals("Payment", StringComparison.OrdinalIgnoreCase);
            TypeForeground = adjustment ? Brush(PresentationPalette.SuccessText) : payment ? Brush(PresentationPalette.PaymentText) : Brush(PresentationPalette.NeutralText);
            TypeBackground = adjustment ? Brush(PresentationPalette.SuccessBackground) : payment ? Brush(PresentationPalette.PaymentBackground) : Brush(PresentationPalette.NeutralBackground);
        }

        private void SetStatusColors()
        {
            if (Status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                StatusForeground = Brush(PresentationPalette.ErrorText);
                StatusBackground = Brush(PresentationPalette.ErrorBackground);
            }
            else if (Status.Equals("Processing", StringComparison.OrdinalIgnoreCase) || Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                StatusForeground = Brush(PresentationPalette.WarningText);
                StatusBackground = Brush(PresentationPalette.WarningBackground);
            }
            else if (Status.Equals("Paid", StringComparison.OrdinalIgnoreCase) || Status.Equals("Verified", StringComparison.OrdinalIgnoreCase))
            {
                StatusForeground = Brush(PresentationPalette.SuccessText);
                StatusBackground = Brush(PresentationPalette.SuccessBackground);
            }
            else
            {
                StatusForeground = Brush(PresentationPalette.NeutralText);
                StatusBackground = Brush(PresentationPalette.NeutralBackground);
            }
        }

        private static string FormatAmount(decimal amount, string type)
        {
            var isPayment = type.Equals("Payment", StringComparison.OrdinalIgnoreCase);
            var sign = amount < 0 ? "-" : amount > 0 && !isPayment ? "+" : string.Empty;
            return $"{sign}${Math.Abs(amount).ToString("N2", CultureInfo.GetCultureInfo("en-US"))}";
        }

        private static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}

