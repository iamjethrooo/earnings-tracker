using FluentIcons.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarningsTracker.Utilities;
using EarningsTracker.Data;

namespace EarningsTracker.Views.Modals
{
    /// <summary>
    /// Interaction logic for ImportResultModal.xaml
    /// </summary>
    public partial class ImportResultModal : UserControl
    {
        public event EventHandler? DoneClicked;
        public event EventHandler? BackClicked;
        public event EventHandler? ConfirmImportClicked;
        private ImportResult? _previewResult;

        public ImportResultModal()
        {
            InitializeComponent();
        }

        public void ShowSuccess(string message)
        {
            ResultBackButton.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Collapsed;

            ResultIcon.Icon = Icon.Checkmark;
            ResultIcon.SetResourceReference(Control.ForegroundProperty, "VerifiedTextColor");

            ResultTitle.Text = "Import complete";
            ResultMessage.Text = message;
        }

        public void ShowError(string message)
        {
            ResultBackButton.Visibility = Visibility.Visible;
            ResultPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Collapsed;

            ResultIcon.Icon = Icon.Dismiss;
            ResultIcon.SetResourceReference(Control.ForegroundProperty, "RejectedTextColor");

            ResultTitle.Text = "Import failed";
            ResultMessage.Text = message;
        }

        public void ShowSavedWarning(string message)
        {
            ShowSuccess(message);
            ResultTitle.Text = "Saved · refresh failed";
            ResultIcon.SetResourceReference(Control.ForegroundProperty, "PendingTextColor");
        }

        public void ShowPreview(ImportResult result)
        {
            _previewResult = result;
            NewSessionsPayout.ItemsSource = result.PayoutOptions;
            NewSessionsPayout.SelectedIndex = 0;
            NewSessionsPayoutPanel.Visibility = result.InsertedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;

            SessionsFoundText.Text = $"{result.TotalSessions} sessions found";

            NewSessionsText.Text = $"✓ {result.InsertedCount} new sessions";
            UpdatedSessionsText.Text = $"↻ {result.UpdatedCount} updates";
            AmbiguousSessionsText.Text = result.UnlinkedCount > 0
                ? $"⚠ {result.UnlinkedCount} will be saved without a game link · {result.AmbiguousCount} conflicts skipped"
                : $"⚠ {result.AmbiguousCount} conflicts skipped";
            InvalidSessionsText.Text = $"✕ {result.InvalidCount} invalid rows excluded";
            SkippedSessionsText.Text = $"↷ {result.SkippedCount} repeated rows skipped";
            TotalEarningsText.Text = $"Session earnings (USD): {CurrencyDisplay.FormatUsd(result.TotalEarnings)}";

            NewSessionsText.Visibility = result.InsertedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdatedSessionsText.Visibility = result.UpdatedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            AmbiguousSessionsText.Visibility = result.AmbiguousCount + result.UnlinkedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            UnlinkedGuidance.Visibility = result.UnlinkedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            InvalidSessionsText.Visibility = result.InvalidCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            SkippedSessionsText.Visibility = result.SkippedCount == 0 ? Visibility.Collapsed : Visibility.Visible;

            ConflictsSection.Header = $"Conflicts ({result.AmbiguousCount})";
            ConflictsSection.Visibility = result.AmbiguousCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            ConflictsSection.IsExpanded = false;
            ConflictRows.ItemsSource = result.AmbiguousSessions.Select(session =>
                $"{session.GameName}\n{session.RecordedDate:MMM d, yyyy h:mm:ss.FFFFFFF tt} · {session.DurationText} · {CurrencyDisplay.FormatUsd(session.Earnings)} · {(session.Status == EarningsTracker.Enums.SessionStatus.NotUploaded ? "Not uploaded" : session.Status.ToString())}").ToList();

            PreviewPanel.Visibility = Visibility.Visible;
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            DoneClicked?.Invoke(this, EventArgs.Empty);
        }

        private void NewSessionsPayout_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_previewResult == null || NewSessionsPayout.SelectedItem is not SessionPayoutOption option) return;
            foreach (var session in _previewResult.NewSessions)
            {
                session.HasExplicitPayoutAssignment = true;
                session.AssignedPayoutId = option.PayoutId;
            }
        }

        private void SummaryBackButton_Click(object sender, RoutedEventArgs e)
        {
            BackClicked?.Invoke(this, EventArgs.Empty);
        }

        private void ConfirmImportButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmImportClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
