using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Controls;

namespace EarningsTracker.Views.Modals
{
    public partial class ImportPayoutsView : UserControl, IDisposable
    {
        private PayoutImporter? _importer;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private readonly DialogWork _work;
        internal Task OperationCompletion { get; private set; } = Task.CompletedTask;
        private PayoutImportResult? _importResult;
        private bool _importCompleted;

        public event EventHandler? ImportCompleted;
        private bool CanImport => !string.IsNullOrWhiteSpace(ImportTextBox.Text);

        public ImportPayoutsView() : this(null) { }

        internal ImportPayoutsView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            InitializeComponent();
            _databaseOptions = databaseOptions;
            _work = new DialogWork(this, BusyStatus);
            Unloaded += (s, e) => Dispose();
        }

        public void Dispose()
        {
            _work.Dispose();
            // Closing a preview must not dispose a context while its worker is still reading.
            if (!_work.IsRunning) _importer?.Dispose();
        }

        private void ParseImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_work.IsRunning && !_work.IsDisposed) OperationCompletion = ParseAsync();
        }

        private async Task ParseAsync()
        {
            if (!CanImport)
            {
                InputValidationMessage.Visibility = Visibility.Visible;
                ImportTextBox.Focus();
                ImportTextBox.BringIntoView();
                return;
            }
            InputValidationMessage.Visibility = Visibility.Collapsed;
            var text = ImportTextBox.Text;

            try
            {
                _importResult = await _work.RunAsync("Preparing payout preview…", () =>
                {
                    _importer?.Dispose();
                    _importer = new PayoutImporter(_databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions));
                    return _importer.PreviewImport(PayoutTextParser.ParseDetailed(text));
                });
                if (_work.IsDisposed) return;
                ShowSummary(_importResult);
                ImportInputPanel.Visibility = Visibility.Collapsed;
                ImportResultContainer.Visibility = Visibility.Visible;
                SkipAmbiguousOption.IsChecked = false;

                var hasAmbiguousMatches = _importResult.AmbiguousPayoutMatches.Count + _importResult.AmbiguousForceVerifiedSessions.Count > 0;
                SkipAmbiguousOption.Visibility = hasAmbiguousMatches ? Visibility.Visible : Visibility.Collapsed;
                ConfirmImportButton.IsEnabled = _importResult.AddedCount + _importResult.UpdatedCount > 0 &&
                    !hasAmbiguousMatches && _importResult.ConflictingRows.Count == 0;
            }
            catch (Exception ex)
            {
                if (_work.IsDisposed) return;
                SetOutcome($"Unable to preview this import: {ex.GetBaseException().Message}", "RejectedTextColor");
                ImportGuidance.Text = "Return to input to correct the text or try again.";
                ImportSummarySections.Children.Clear();
                SkipAmbiguousOption.Visibility = Visibility.Collapsed;
                ConfirmImportButton.IsEnabled = false;
                ImportInputPanel.Visibility = Visibility.Collapsed;
                ImportResultContainer.Visibility = Visibility.Visible;
            }
            finally { if (_work.IsDisposed) _importer?.Dispose(); }
        }

        private void SummaryBackButton_Click(object sender, RoutedEventArgs e)
        {
            ImportInputPanel.Visibility = Visibility.Visible;
            ImportResultContainer.Visibility = Visibility.Collapsed;
        }

        private void ConfirmImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_work.IsRunning && !_work.IsDisposed) OperationCompletion = ConfirmImportAsync();
        }

        private async Task ConfirmImportAsync()
        {
            if (_importCompleted)
            {
                try { ImportCompleted?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    SetOutcome($"Changes were saved, but the window could not be updated.\n{ex.GetBaseException().Message}", "PendingTextColor");
                }
                return;
            }

            if (_importResult == null)
                return;

            string? refreshWarning = null;
            try
            {
                _importResult.SkipAmbiguousRecords = SkipAmbiguousOption.IsChecked == true;
                await _work.RunAsync("Saving payouts…", () => { _importer!.ImportPayouts(_importResult); return true; }, committing: true);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                if (_work.IsDisposed) return;
                SetOutcome($"Import failed. No changes were committed.\n{ex.GetBaseException().Message}", "RejectedTextColor");
                ImportGuidance.Text = "Return to input and preview again before retrying.";
                ConfirmImportButton.IsEnabled = false;
                return;
            }
            finally { if (_work.IsDisposed) _importer?.Dispose(); }

            // Retain completion before building the summary so a display error cannot enable a retry.
            _importCompleted = true;
            if (_work.IsDisposed) return;
            SkipAmbiguousOption.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            ConfirmImportButton.Content = "Done";
            ConfirmImportButton.IsEnabled = true;
            try
            {
                ShowSummary(_importResult, completed: true);
                if (refreshWarning != null)
                    SetOutcome(refreshWarning, "PendingTextColor");
            }
            catch (Exception ex)
            {
                SetOutcome($"Changes were saved, but the import summary could not be displayed.\n{ex.GetBaseException().Message}", "PendingTextColor");
            }
        }

        private void SkipAmbiguousOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_importResult == null)
                return;

            var hasAmbiguousMatches = _importResult.AmbiguousPayoutMatches.Count + _importResult.AmbiguousForceVerifiedSessions.Count > 0;
            ConfirmImportButton.IsEnabled = _importResult.AddedCount + _importResult.UpdatedCount > 0 &&
                _importResult.ConflictingRows.Count == 0 &&
                (!hasAmbiguousMatches || SkipAmbiguousOption.IsChecked == true);
        }

        private void ShowSummary(PayoutImportResult result, bool completed = false)
        {
            ImportSummarySections.Children.Clear();
            SetOutcome(completed ? "Import saved." : "Review the summary before importing.", completed ? "VerifiedTextColor" : "DefaultTextColor");
            ImportGuidance.Text = completed ? "Saved changes retain their source evidence and audit history. Choose Done to close." :
                result.ConflictingRows.Count > 0 ? "Conflicting rows block this import. Return to input to correct them and preview again." :
                result.AmbiguousPayoutMatches.Count + result.AmbiguousForceVerifiedSessions.Count > 0 ? "Ambiguous rows cannot be matched safely. Review Skipped below, then explicitly choose to skip them or return to input. No changes have been saved." :
                result.AddedCount + result.UpdatedCount == 0 ? "No additions or updates are available. Review skipped or failed rows, or return to input." :
                "Only added and updated records will be saved. Review skipped and failed rows; signed USD amounts are preserved.";

            AddSection("Added", result.AddedCount,
                result.NewPayouts.Select(p => $"{p.Date:d} · {p.Type} — {p.Description} ({CurrencyDisplay.FormatUsd(p.Amount)}, {p.Status})"), "VerifiedTextColor");

            var updatedRows = result.Updates.Select(update =>
                    $"{update.Existing.Date:d} {update.Existing.Type} — {update.Existing.Description} ({CurrencyDisplay.FormatUsd(update.Existing.Amount)}, {update.Existing.Status}) → {update.Imported.Date:d} {update.Imported.Type} — {update.Imported.Description} ({CurrencyDisplay.FormatUsd(update.Imported.Amount)}, {update.Imported.Status})")
                .Concat(result.ForceVerifiedSessionUpdates.Select(update =>
                    $"{update.Session.GameName}, recorded {update.Session.RecordedDate:d} {update.Session.RecordedDate:t}, posted {update.Session.PostedDate:d}"));
            AddSection("Updated", result.UpdatedCount, updatedRows);

            AddSection("Unchanged", result.UnchangedCount,
                result.Duplicates.Select(p => $"{p.Date:d} · {p.Type} — {p.Description} ({CurrencyDisplay.FormatUsd(p.Amount)}, {p.Status})"));

            var skippedRows = result.SkippedRows
                .Concat(result.ConflictingRows.Select(row => $"Conflicting import rows: {row}"))
                .Concat(result.AmbiguousPayoutMatches.Select(item =>
                    $"Ambiguous payout '{item.Imported.Description}': matches {string.Join("; ", item.Candidates.Select(p => $"#{p.Id} {p.Date:d} {CurrencyDisplay.FormatUsd(p.Amount)} {p.Status}"))}"))
                .Concat(result.AmbiguousForceVerifiedSessions.Select(session =>
                    $"Ambiguous session match: {session.GameName} recorded {session.RecordedDate:d} {session.RecordedDate:t}"))
                .Concat(result.UnmatchedForceVerifiedSessions.Select(session =>
                    $"Unmatched force-verified session row: {session.GameName} recorded {session.RecordedDate:d} {session.RecordedDate:t}"));
            AddSection("Skipped", result.SkippedCount, skippedRows, "PendingTextColor", !completed && result.SkippedCount > 0);

            AddSection("Failed", result.FailedRows.Count, result.FailedRows, "RejectedTextColor", !completed && result.FailedRows.Count > 0);
        }

        private void SetOutcome(string text, string foreground)
        {
            ImportOutcome.Text = text;
            ImportOutcome.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        }

        private void AddSection(string title, int count, IEnumerable<string> details, string foreground = "DefaultTextColor", bool expanded = false)
        {
            var rows = details.ToList();
            var expander = new Expander
            {
                Header = new TextBlock
                {
                    Text = $"{title} ({count})",
                    Style = (Style)FindResource("CompactSectionHeading"),
                    Foreground = (Brush)FindResource(count == 0 ? "SecondaryTextColor" : foreground),
                    Margin = new Thickness(0)
                },
                IsExpanded = expanded,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = (Thickness)FindResource("CompactPanelPadding"),
                Background = (Brush)FindResource("SurfaceColor"),
                Foreground = (Brush)FindResource("DefaultTextColor"),
                BorderBrush = (Brush)FindResource("DefaultBorderColor"),
                BorderThickness = new Thickness(1)
            };

            // A finite viewport keeps recycling active inside the modal's outer scroller.
            var list = new ListBox
            {
                ItemsSource = rows,
                MaxHeight = 240,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 0)
            };
            ScrollViewer.SetCanContentScroll(list, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            list.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),
                new Style(typeof(System.Windows.Controls.Primitives.ScrollBar), (Style)FindResource("VerticalScrollBar")));
            System.Windows.Automation.AutomationProperties.SetName(list, $"{title} import records");
            var rowStyle = new Style(typeof(ListBoxItem), (Style)FindResource("ItemButton"));
            rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
            rowStyle.Setters.Add(new Setter(Control.FontSizeProperty, FindResource("FontSizeCompactBody")));
            rowStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
            list.ItemContainerStyle = rowStyle;
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.ForegroundProperty, FindResource("DefaultTextColor"));
            list.ItemTemplate = new DataTemplate { VisualTree = text };
            expander.Content = rows.Count == 0 ? new TextBlock
            {
                Text = "No records.",
                Style = (Style)FindResource("ItemSubtitle"),
                Margin = new Thickness(0, 8, 0, 0)
            } : list;
            ImportSummarySections.Children.Add(expander);
        }
    }
}
