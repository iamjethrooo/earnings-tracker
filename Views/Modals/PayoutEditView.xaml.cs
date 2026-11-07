using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using EarningsTracker.Controls;
using EarningsTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Views.Modals
{
    public partial class PayoutEditView : UserControl, IDisposable
    {
        private readonly Payout? _existing;
        private readonly PayoutService.EditState? _expectedState;
        private bool _saveCompleted;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private readonly DialogWork _work;
        private PayoutCoveragePreview? _coverage;
        internal Task OperationCompletion { get; private set; } = Task.CompletedTask;
        public event EventHandler? Saved;

        public PayoutEditView(Payout? existing = null) : this(existing, null) { }

        internal PayoutEditView(Payout? existing, DbContextOptions<AppDbContext>? databaseOptions)
        {
            InitializeComponent();
            _databaseOptions = databaseOptions;
            _work = new DialogWork(this, BusyStatus);
            Unloaded += (_, _) => Dispose();
            _existing = existing;
            _expectedState = existing == null ? null : PayoutService.CaptureEditState(existing);
            PayoutType.ItemsSource = new[] { "Payment", "Bonus", "Earnings correction", "Other" };
            Status.ItemsSource = new[] { "Paid", "Processing", "Failed", "Pending", "Verified" };
            PayoutDate.SelectedDate = existing?.Date ?? DateTime.Today;
            PayoutType.SelectedItem = existing?.Type ?? "Payment";
            Description.Text = existing?.Description ?? string.Empty;
            Status.SelectedItem = existing?.Status ?? "Paid";
            Amount.Text = existing?.Amount.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
            // Conflicting historical fields need an explicit choice, not a preselected correction.
            IncludedRadio.IsChecked = existing?.ReconciliationState == "IncludedInSessions"
                && existing.IsAdjustmentIncludedInSessionEarnings;
            SeparateRadio.IsChecked = existing?.ReconciliationState == "Separate"
                && !existing.IsAdjustmentIncludedInSessionEarnings;
            AdjustmentOptions.Visibility = PayoutType.SelectedItem?.ToString() is "Bonus" or "Earnings correction" ? Visibility.Visible : Visibility.Collapsed;
            PayoutType.SelectionChanged += (s, e) =>
            {
                var isAdjustment = PayoutType.SelectedItem?.ToString() is "Bonus" or "Earnings correction";
                AdjustmentOptions.Visibility = isAdjustment ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_work.IsRunning && !_work.IsDisposed) OperationCompletion = SaveAsync();
        }

        public void Dispose() => _work.Dispose();

        private async Task SaveAsync()
        {
            if (_saveCompleted)
            {
                CompleteSave();
                return;
            }
            ValidationMessage.Visibility = Visibility.Collapsed;
            if (PayoutDate.SelectedDate is not DateTime date || PayoutType.SelectedItem is not string type || Status.SelectedItem is not string status)
            {
                Control field = !PayoutDate.SelectedDate.HasValue ? PayoutDate : PayoutType.SelectedItem == null ? PayoutType : Status;
                ShowValidation("Choose a date, type, and status.", field);
                return;
            }
            if (!decimal.TryParse(Amount.Text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount))
            {
                ShowValidation("Enter a valid signed USD amount, such as 23.84 or -2.50.", Amount);
                Amount.SelectAll();
                return;
            }

            var payout = new Payout
            {
                Date = date,
                Type = type,
                Description = Description.Text,
                Status = status,
                Amount = amount,
                IsAdjustmentIncludedInSessionEarnings = IncludedRadio.IsChecked == true,
                ReconciliationState = IncludedRadio.IsChecked == true ? "IncludedInSessions" : SeparateRadio.IsChecked == true ? "Separate" : "NeedsReview"
            };

            if (type is "Bonus" or "Earnings correction" && IncludedRadio.IsChecked != true && SeparateRadio.IsChecked != true)
            {
                ShowValidation("Choose whether this adjustment is already included in session earnings before saving.", IncludedRadio);
                return;
            }

            string? refreshWarning = null;
            try
            {
                PayoutService.Validate(payout);
                if (PayoutAssignments.NeedsCoverage(payout, _existing))
                {
                    if (_coverage == null || _coverage.PaymentState != (payout.Date, payout.Type, payout.Status, payout.Amount))
                    {
                        _coverage = await _work.RunAsync("Preparing session coverage…", () =>
                        {
                            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                            return PayoutAssignments.Preview(db, payout);
                        });
                        if (_work.IsDisposed) return;
                        CoveragePanel.Content = new PayoutCoverageView { DataContext = _coverage };
                        SaveButton.Content = "Save payout and coverage";
                        ValidationMessage.Text = "Review the sessions below, then save the payout and coverage.";
                        ValidationMessage.SetResourceReference(TextBlock.ForegroundProperty, "PendingTextColor");
                        ValidationMessage.Visibility = Visibility.Visible;
                        SaveButton.Focus();
                        SaveButton.BringIntoView();
                        return;
                    }
                }
                else { _coverage = null; CoveragePanel.Content = null; }
                var reason = Reason.Text;
                await _work.RunAsync("Saving payout and coverage…", () =>
                {
                    if (_existing == null)
                        PayoutService.Create(payout, reason, _databaseOptions, _coverage);
                    else
                        PayoutService.Update(_existing.Id, payout, reason, _databaseOptions, _expectedState, _coverage);
                    return true;
                }, committing: true);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                _coverage = null;
                CoveragePanel.Content = null;
                ShowValidation(ex.Message, SaveButton);
                return;
            }

            _saveCompleted = true;
            if (_work.IsDisposed) return;
            SaveButton.Content = "Done";
            if (refreshWarning != null)
                MessageBox.Show(refreshWarning, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            CompleteSave();
        }

        private void ShowValidation(string message, Control field)
        {
            ValidationMessage.Text = message;
            ValidationMessage.SetResourceReference(TextBlock.ForegroundProperty, "RejectedTextColor");
            ValidationMessage.Visibility = Visibility.Visible;
            field.Focus();
            field.BringIntoView();
        }

        private void CompleteSave()
        {
            try { Saved?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                MessageBox.Show($"The payout was saved, but the window could not be updated.\n{ex.GetBaseException().Message}",
                    "Saved — display update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
