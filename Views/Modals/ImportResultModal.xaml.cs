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

            ResultTitle.Text = "Import Complete";
            ResultMessage.Text = message;
        }

        public void ShowError(string message)
        {
            ResultBackButton.Visibility = Visibility.Visible;
            ResultPanel.Visibility = Visibility.Visible;
            PreviewPanel.Visibility = Visibility.Collapsed;

            ResultIcon.Icon = Icon.Dismiss;
            ResultIcon.SetResourceReference(Control.ForegroundProperty, "RejectedTextColor");

            ResultTitle.Text = "Import Failed";
            ResultMessage.Text = message;
        }

        public void ShowSavedWarning(string message)
        {
            ShowSuccess(message);
            ResultTitle.Text = "Import Saved — Display Update Failed";
            ResultIcon.SetResourceReference(Control.ForegroundProperty, "PendingTextColor");
        }

        public void ShowPreview(ImportResult result)
        {
            ResultPanel.Visibility = Visibility.Collapsed;
            PreviewPanel.Visibility = Visibility.Visible;

            SessionsFoundText.Text = $"{result.TotalSessions} sessions found";

            NewSessionsText.Text = $"✓ {result.InsertedCount} new sessions";
            UpdatedSessionsText.Text = $"↻ {result.UpdatedCount} existing sessions will be updated";
            AmbiguousSessionsText.Text = $"⚠ {result.AmbiguousCount} ambiguous";
            InvalidSessionsText.Text = $"✕ {result.InvalidCount} invalid";
            SkippedSessionsText.Text = $"↷ {result.SkippedCount} repeated rows skipped";
            TotalEarningsText.Text = $"Total session earnings: {CurrencyDisplay.FormatUsd(result.TotalEarnings)}";

            NewSessionsText.Visibility = result.InsertedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdatedSessionsText.Visibility = result.UpdatedCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            AmbiguousSessionsText.Visibility = result.AmbiguousCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            InvalidSessionsText.Visibility = result.InvalidCount == 0 ? Visibility.Collapsed : Visibility.Visible;
            SkippedSessionsText.Visibility = result.SkippedCount == 0 ? Visibility.Collapsed : Visibility.Visible;

            PreviewPanel.Visibility = Visibility.Visible;
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            DoneClicked?.Invoke(this, EventArgs.Empty);
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
