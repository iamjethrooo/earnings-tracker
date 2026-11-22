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
        private IReadOnlyList<EarningsTracker.Models.Game> _games = [];

        public ImportResultModal()
        {
            InitializeComponent();
        }

        public void ShowSuccess(string message)
        {
            ResultBackButton.Visibility = Visibility.Collapsed;
            ResultDoneButton.Content = "Done";
            ResultDoneButton.Style = (Style)FindResource("PrimaryButton");
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
            ResultBackButton.Content = "Fix input";
            ResultBackButton.Style = (Style)FindResource("PrimaryButton");
            ResultDoneButton.Content = "Close";
            ResultDoneButton.Style = (Style)FindResource("SecondaryButton");
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

        public void ShowPreview(ImportResult result, IReadOnlyList<EarningsTracker.Models.Game>? games = null)
        {
            _previewResult = result;
            _games = games ?? [];
            NewSessionsPayout.ItemsSource = result.PayoutOptions;
            NewSessionsPayout.SelectedIndex = 0;
            NewSessionsPayoutPanel.Visibility = result.InsertedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;

            SessionsFoundText.Text = $"{result.TotalSessions} {(result.TotalSessions == 1 ? "session" : "sessions")} found";

            NewSessionsText.Text = $"✓ {result.InsertedCount} new {(result.InsertedCount == 1 ? "session" : "sessions")}";
            UpdatedSessionsText.Text = $"↻ {result.UpdatedCount} {(result.UpdatedCount == 1 ? "update" : "updates")}";
            AmbiguousSessionsText.Text = result.UnlinkedCount > 0
                ? $"⚠ {result.UnlinkedCount} will be saved without a game link · {result.AmbiguousCount} {(result.AmbiguousCount == 1 ? "conflict" : "conflicts")} skipped"
                : $"⚠ {result.AmbiguousCount} {(result.AmbiguousCount == 1 ? "conflict" : "conflicts")} skipped";
            InvalidSessionsText.Text = $"✕ {result.InvalidCount} invalid {(result.InvalidCount == 1 ? "row" : "rows")} excluded";
            SkippedSessionsText.Text = $"↷ {result.SkippedCount} repeated {(result.SkippedCount == 1 ? "row" : "rows")} skipped";
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
            ReviewRows.ItemsSource = EarningsTracker.ViewModels.SessionImportReviewItem.Create(result, _games);
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
            ReviewRows.ItemsSource = EarningsTracker.ViewModels.SessionImportReviewItem.Create(_previewResult, _games);
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
