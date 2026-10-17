using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;

namespace EarningsTracker.Views.Modals
{
    public partial class PayoutEditView : UserControl
    {
        private readonly Payout? _existing;
        private readonly PayoutService.EditState? _expectedState;
        private bool _saveCompleted;
        public event EventHandler? Saved;

        public PayoutEditView(Payout? existing = null)
        {
            InitializeComponent();
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
                if (_existing == null)
                    PayoutService.Create(payout, Reason.Text);
                else
                    PayoutService.Update(_existing.Id, payout, Reason.Text, expectedState: _expectedState);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                ShowValidation(ex.Message, SaveButton);
                return;
            }

            _saveCompleted = true;
            SaveButton.Content = "Done";
            if (refreshWarning != null)
                MessageBox.Show(refreshWarning, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            CompleteSave();
        }

        private void ShowValidation(string message, Control field)
        {
            ValidationMessage.Text = message;
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
