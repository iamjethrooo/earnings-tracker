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

        public bool IsCompactReview
        {
            get => (bool)GetValue(IsCompactReviewProperty);
            private set => SetValue(IsCompactReviewProperty, value);
        }
        public static readonly DependencyProperty IsCompactReviewProperty = DependencyProperty.Register(
            nameof(IsCompactReview), typeof(bool), typeof(ImportPayoutsView), new PropertyMetadata(false));
        private void Review_SizeChanged(object sender, SizeChangedEventArgs e) => IsCompactReview = e.NewSize.Width < 680;

        internal sealed record PayoutReviewRow(PayoutHistoryItem Row, string Before = "")
        {
            public string AccessibleName => $"{Row.PostedText} · {Row.Type} · {Row.Description} · {Row.AmountText} · {Row.Status} {Before}";
            public override string ToString() => AccessibleName;
        }

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

        private Modal? FindParentModal()
        {
            DependencyObject? parent = this;
            while (parent != null)
            {
                if (parent is Modal modal) return modal;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void CancelImport_Click(object sender, RoutedEventArgs e)
        {
            if (!_work.IsRunning && !_work.IsDisposed) FindParentModal()?.Close();
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
            Modal.SetContentMaxWidth(this, 920);
            if (FindParentModal() is Modal modal) modal.Title = "Import payouts";
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
            Modal.SetContentMaxWidth(this, 940);
            if (FindParentModal() is Modal modal) modal.Title = completed ? "Import complete" : "Review payout import";
            var total = result.AddedCount + result.UpdatedCount + result.UnchangedCount + result.SkippedCount + result.FailedRows.Count;
            ImportCounts.Text = $"{total} records found · {result.AddedCount} new · {result.UpdatedCount} {(result.UpdatedCount == 1 ? "update" : "updates")} · {result.UnchangedCount} unchanged · {result.SkippedCount} skipped";
            if (result.FailedRows.Count > 0) ImportCounts.Text += $" · {result.FailedRows.Count} failed";
            ImportSummarySections.Children.Clear();
            SetOutcome(completed ? "Import saved." : "Import preview", completed ? "VerifiedTextColor" : "DefaultTextColor");
            ImportOutcome.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
            ImportGuidance.Text = completed ? "" :
                result.ConflictingRows.Count > 0 ? "Conflicting rows must be corrected before importing." :
                result.AmbiguousPayoutMatches.Count + result.AmbiguousForceVerifiedSessions.Count > 0 ? "Ambiguous rows: review Skipped, then skip them or correct the input. Nothing saved yet." :
                result.AddedCount + result.UpdatedCount == 0 ? "No additions or updates. Review skipped or failed rows." :
                "";

            ImportGuidance.Visibility = string.IsNullOrEmpty(ImportGuidance.Text) ? Visibility.Collapsed : Visibility.Visible;

            AddSection("Added", result.AddedCount,
                result.NewPayouts.Select(p => $"{p.Date:d} · {p.Type} — {p.Description} ({CurrencyDisplay.FormatUsd(p.Amount)}, {p.Status})"), "VerifiedTextColor",
                reviewRows: result.NewPayouts.Select(p => new PayoutReviewRow(PayoutHistoryItem.FromPayout(p))));

            var updatedRows = result.Updates.Select(update =>
                    $"{update.Existing.Date:d} {update.Existing.Type} — {update.Existing.Description} ({CurrencyDisplay.FormatUsd(update.Existing.Amount)}, {update.Existing.Status}) → {update.Imported.Date:d} {update.Imported.Type} — {update.Imported.Description} ({CurrencyDisplay.FormatUsd(update.Imported.Amount)}, {update.Imported.Status})")
                .Concat(result.ForceVerifiedSessionUpdates.Select(update =>
                    $"{update.Session.GameName}, recorded {update.Session.RecordedDate:d} {update.Session.RecordedDate:t}, posted {update.Session.PostedDate:d}"));
            AddSection("Updated", result.UpdatedCount, updatedRows,
                reviewRows: result.Updates.Select(update => new PayoutReviewRow(PayoutHistoryItem.FromPayout(update.Imported),
                    $"Saved: {update.Existing.Date:d} · {update.Existing.Type} · {update.Existing.Description} · {CurrencyDisplay.FormatUsd(update.Existing.Amount)} · {update.Existing.Status}"))
                    .Concat(result.ForceVerifiedSessionUpdates.Select(update => new PayoutReviewRow(PayoutHistoryItem.FromSession(update.Session),
                        $"Force verification · recorded {update.Session.RecordedDate:d} {update.Session.RecordedDate:t} · posted {update.Session.PostedDate:d}"))));

            AddSection("Unchanged", result.UnchangedCount,
                result.Duplicates.Select(p => $"{p.Date:d} · {p.Type} — {p.Description} ({CurrencyDisplay.FormatUsd(p.Amount)}, {p.Status})"),
                reviewRows: result.Duplicates.Select(p => new PayoutReviewRow(PayoutHistoryItem.FromPayout(p))));

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
            if (!completed)
                foreach (var coverage in result.CoveragePreviews)
                    ImportSummarySections.Children.Add(new PayoutCoverageView { DataContext = coverage });
        }

        private void SetOutcome(string text, string foreground)
        {
            ImportOutcome.Visibility = Visibility.Visible;
            ImportOutcome.Text = text;
            ImportOutcome.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        }

        private void AddSection(string title, int count, IEnumerable<string> details, string foreground = "DefaultTextColor", bool expanded = false, IEnumerable<PayoutReviewRow>? reviewRows = null)
        {
            var rows = details.ToList();
            var formattedRows = reviewRows?.ToList();
            if (count == 0)
            {
                ImportSummarySections.Children.Add(new TextBlock
                {
                    Text = $"{title} (0)",
                    Style = (Style)FindResource("ItemSubtitle"),
                    Margin = new Thickness(0, 0, 0, 8)
                });
                return;
            }
            var expander = new Expander
            {
                Header = new TextBlock
                {
                    Text = $"{title} ({count})",
                    Style = (Style)FindResource("CompactSectionHeading"),
                    Foreground = (Brush)FindResource(count == 0 ? "SecondaryTextColor" : foreground),
                    Margin = new Thickness(0)
                },
                IsExpanded = expanded || title is "Added" or "Updated",
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(0, 12, 0, 0),
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("DefaultTextColor"),
                BorderBrush = (Brush)FindResource("DefaultBorderColor"),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };

            // A finite viewport keeps recycling active inside the modal's outer scroller.
            var list = new ListBox
            {
                ItemsSource = formattedRows == null ? rows : formattedRows,
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
            var rowStyle = new Style(typeof(ListBoxItem), (Style)FindResource("DialogReviewItem"));
            rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
            rowStyle.Setters.Add(new Setter(Control.FontSizeProperty, FindResource("FontSizeBody")));
            rowStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
            if (formattedRows != null)
                rowStyle.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,
                    new System.Windows.Data.Binding(nameof(PayoutReviewRow.AccessibleName))));
            list.ItemContainerStyle = rowStyle;
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            text.SetValue(TextBlock.ForegroundProperty, FindResource("DefaultTextColor"));
            if (formattedRows == null)
                list.ItemTemplate = new DataTemplate { VisualTree = text };
            if (formattedRows != null)
            {
                var listStyle = new Style(typeof(ListBox), (Style)FindResource(typeof(ListBox)));
                listStyle.Setters.Add(new Setter(ItemsControl.ItemTemplateProperty, FindResource("PayoutReviewRow")));
                var compact = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(IsCompactReview)) { Source = this }, Value = true };
                compact.Setters.Add(new Setter(ItemsControl.ItemTemplateProperty, FindResource("CompactPayoutReviewRow")));
                listStyle.Triggers.Add(compact);
                list.Style = listStyle;
            }
            expander.Content = list;
            ImportSummarySections.Children.Add(expander);
        }
    }
}
