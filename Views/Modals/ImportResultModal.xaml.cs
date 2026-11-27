using FluentIcons.Common;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarningsTracker.Utilities;
using EarningsTracker.Data;
using EarningsTracker.Controls;
using EarningsTracker.Models;

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

        private IReadOnlyList<GameMetadata> _metadata = [];
        public bool IsCompactReview
        {
            get => (bool)GetValue(IsCompactReviewProperty);
            private set => SetValue(IsCompactReviewProperty, value);
        }
        public static readonly DependencyProperty IsCompactReviewProperty = DependencyProperty.Register(
            nameof(IsCompactReview), typeof(bool), typeof(ImportResultModal), new PropertyMetadata(false));
        private void Review_SizeChanged(object sender, SizeChangedEventArgs e) => IsCompactReview = e.NewSize.Width < 700;

        private void SetPresentation(double width, string? title = null)
        {
            DependencyObject? parent = this;
            while (parent != null)
            {
                if (parent is Modal modal)
                {
                    if (modal.ModalContent is DependencyObject content) Modal.SetContentMaxWidth(content, width);
                    if (title != null) modal.Title = title;
                    break;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
        }

        public ImportResultModal()
        {
            InitializeComponent();
        }

        public void ShowSuccess(string message)
        {
            SetPresentation(600);
            ResultSummary.Visibility = _previewResult == null ? Visibility.Collapsed : Visibility.Visible;
            if (_previewResult != null)
            {
                string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";
                var summary = new List<string> { Count(_previewResult.InsertedCount, "session", "sessions") + " added" };
                if (_previewResult.UpdatedCount > 0) summary.Add(Count(_previewResult.UpdatedCount, "session", "sessions") + " updated");
                if (_previewResult.SkippedCount > 0) summary.Add(Count(_previewResult.SkippedCount, "repeated row", "repeated rows") + " skipped");
                ResultSummary.Text = string.Join("\n", summary);
                var warnings = new List<string>();
                var excluded = _previewResult.AmbiguousCount + _previewResult.InvalidCount;
                if (excluded > 0) warnings.Add(Count(excluded, "recording", "recordings") + " excluded. Review excluded recordings before trying again.");
                if (_previewResult.UnlinkedCount > 0) warnings.Add(Count(_previewResult.UnlinkedCount, "session", "sessions") + " saved without a game link. Link them in Sessions → Unlinked.");
                ResultExclusions.Text = string.Join("\n", warnings);
                ResultExclusions.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else ResultExclusions.Visibility = Visibility.Collapsed;
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
            SetPresentation(600);
            ResultSummary.Visibility = Visibility.Collapsed;
            ResultExclusions.Visibility = Visibility.Collapsed;
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

        public void ShowPreview(ImportResult result, IReadOnlyList<Game>? games = null, IReadOnlyList<GameMetadata>? metadata = null)
        {
            SetPresentation(1000, "Review session import");
            _previewResult = result;
            _metadata = metadata ?? [];
            _games = games ?? [];
            NewSessionsPayout.ItemsSource = result.PayoutOptions;
            NewSessionsPayout.SelectedIndex = 0;
            NewSessionsPayoutPanel.Visibility = result.InsertedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;

            SessionsFoundText.Text = $"{result.TotalSessions} {(result.TotalSessions == 1 ? "session" : "sessions")} found";

            NewSessionsText.Text = $"{result.InsertedCount} new {(result.InsertedCount == 1 ? "session" : "sessions")}";
            UpdatedSessionsText.Text = $"{result.UpdatedCount} {(result.UpdatedCount == 1 ? "update" : "updates")}";
            AmbiguousSessionsText.Text = result.UnlinkedCount > 0
                ? $"{result.UnlinkedCount} will be saved without a game link · {result.AmbiguousCount} {(result.AmbiguousCount == 1 ? "conflict" : "conflicts")} skipped"
                : $"{result.AmbiguousCount} {(result.AmbiguousCount == 1 ? "conflict" : "conflicts")} skipped";
            InvalidSessionsText.Text = $"{result.InvalidCount} invalid {(result.InvalidCount == 1 ? "row" : "rows")} excluded";
            SkippedSessionsText.Text = $"{result.SkippedCount} repeated {(result.SkippedCount == 1 ? "row" : "rows")} skipped";
            TotalEarningsText.Text = $"Imported recorded amounts (USD): {CurrencyDisplay.FormatUsd(result.TotalEarnings)}";

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
            ReviewRows.ItemsSource = EarningsTracker.ViewModels.SessionImportReviewItem.Create(result, _games, _metadata);
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
            ReviewRows.ItemsSource = EarningsTracker.ViewModels.SessionImportReviewItem.Create(_previewResult, _games, _metadata);
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
