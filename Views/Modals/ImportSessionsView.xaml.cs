using EarningsTracker.Controls;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarningsTracker.Views.Modals;
using System.Diagnostics;
using EarningsTracker.Exceptions;
using Microsoft.EntityFrameworkCore;


namespace EarningsTracker.Views.Modals
{
    /// <summary>
    /// Interaction logic for ImportSessionsView.xaml
    /// </summary>
    public partial class ImportSessionsView : UserControl, IDisposable
    {
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;
        private readonly DialogWork _work;
        internal Task OperationCompletion { get; private set; } = Task.CompletedTask;

        public bool CanImport => !string.IsNullOrWhiteSpace(ImportTextBox.Text);

        public event EventHandler? ImportCompleted;
        private ImportResult? importResult;
        private readonly SessionImporter importer;
        private bool _importCompleted;
        private int? _errorLine;
        public ImportSessionsView() : this(null) { }

        internal ImportSessionsView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            InitializeComponent();
            _databaseOptions = databaseOptions;
            _work = new DialogWork(this, BusyStatus);
            Unloaded += (_, _) => Dispose();
            importer = new SessionImporter(databaseOptions);



            ImportResultModal.DoneClicked += (sender, e) =>
            {
                try { ImportCompleted?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    if (_importCompleted)
                        ImportResultModal.ShowSavedWarning($"Sessions were saved, but the window could not be updated.\n{ex.GetBaseException().Message}");
                    else
                        ImportResultModal.ShowError($"Unable to close this window: {ex.GetBaseException().Message}");
                }
            };

            ImportResultModal.BackClicked += (sender, e) =>
            {
                SummaryBackButton_Click();
            };

            ImportResultModal.ConfirmImportClicked += (sender, e) =>
            {
                ConfirmImportButton_Click();
            };
        }

        public void Dispose() => _work.Dispose();

        private void CancelImport_Click(object sender, RoutedEventArgs e)
        {
            if (!_work.IsRunning && !_work.IsDisposed) FindParentModal()?.Close();
        }

        private void ParseImportButton_Click(object sender, RoutedEventArgs e) { if (!_work.IsRunning && !_work.IsDisposed) OperationCompletion = ParseAsync(); }

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

            string importText = ImportTextBox.Text;

            try
            {
                ImportInputPanel.Visibility = Visibility.Collapsed;
                _errorLine = null;
                var preview = await _work.RunAsync("Preparing session preview…", () =>
                {
                    using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                    var games = db.Games.AsNoTracking().ToList();
                    return (Result: importer.PreviewImport(SessionTextParser.Parse(importText, games, null)), Games: games, Metadata: db.GameMetadata.AsNoTracking().ToList());
                });
                if (_work.IsDisposed) return;

                importResult = preview.Result;
                ImportResultModal.ShowPreview(importResult, preview.Games, preview.Metadata);
                ImportResultContainer.Visibility = Visibility.Visible;
            } catch (Exception ex)
            {
                if (_work.IsDisposed) return;
                Debug.WriteLine(ex.Message);
                var line = System.Text.RegularExpressions.Regex.Match(ex.Message, @"\bLine (\d+):");
                if (line.Success && int.TryParse(line.Groups[1].Value, out var number)) _errorLine = number - 1;
                ImportResultModal.ShowError($"Unable to parse the imported data: {ex.Message}");
                ImportResultContainer.Visibility = Visibility.Visible;
            }
        }

        private void SummaryBackButton_Click()
        {
            Modal.SetContentMaxWidth(this, 920);
            if (FindParentModal() is Modal modal) modal.Title = "Import sessions";
            ImportInputPanel.Visibility = Visibility.Visible;
            ImportResultContainer.Visibility = Visibility.Collapsed;
            ImportTextBox.Focus();
            if (_errorLine is int line && line >= 0)
            {
                // Parser line numbers refer to source lines, not visually wrapped TextBox lines.
                var lines = System.Text.RegularExpressions.Regex.Matches(ImportTextBox.Text, @"[^\r\n]*(?:\r\n|\r|\n|$)");
                if (line < lines.Count)
                {
                    var sourceLine = lines[line];
                    ImportTextBox.Select(sourceLine.Index, sourceLine.Value.TrimEnd('\r', '\n').Length);
                    ImportTextBox.UpdateLayout();
                    var visualLine = ImportTextBox.GetLineIndexFromCharacterIndex(sourceLine.Index);
                    if (visualLine >= 0) ImportTextBox.ScrollToLine(visualLine);
                }
            }
        }

        private void ConfirmImportButton_Click() { if (!_work.IsRunning && !_work.IsDisposed) OperationCompletion = ConfirmImportAsync(); }

        private async Task ConfirmImportAsync()
        {
            if (_importCompleted)
                return;
            string? refreshWarning = null;
            try
            {
                if (importResult == null)
                    throw new InvalidOperationException("Parse the session list before importing it.");
                await _work.RunAsync("Saving sessions…", () => { importer.ImportSessions(importResult); return true; }, committing: true);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                if (_work.IsDisposed) return;
                Debug.WriteLine(ex);
                ImportResultModal.ShowError($"Unable to save imported sessions: {ex.GetBaseException().Message}");
                ImportResultContainer.Visibility = Visibility.Visible;
                return;
            }

            _importCompleted = true;
            if (_work.IsDisposed) return;
            try
            {
                FindParentModal()?.SetHeaderVisible(false);
                if (refreshWarning == null)
                {
                    var count = importResult!.InsertedCount + importResult.UpdatedCount;
                    ImportResultModal.ShowSuccess($"{count} {(count == 1 ? "session" : "sessions")} imported.");
                }
                else
                    ImportResultModal.ShowSavedWarning(refreshWarning);
                ImportResultContainer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ImportResultModal.ShowSavedWarning($"Sessions were saved, but the summary could not be displayed.\n{ex.GetBaseException().Message}");
                ImportResultContainer.Visibility = Visibility.Visible;
            }
        }

        private Modal? FindParentModal()
        {
            DependencyObject? current = this;

            while (current != null)
            {
                if (current is Modal modal)
                    return modal;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
