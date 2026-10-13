using nerve_earnings_tracker.Controls;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using nerve_earnings_tracker.Views.Modals;
using System.Diagnostics;
using nerve_earnings_tracker.Exceptions;
using Microsoft.EntityFrameworkCore;


namespace nerve_earnings_tracker.Views.Modals
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
                importResult = await _work.RunAsync("Preparing session preview…", () =>
                {
                    using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);
                    var games = db.Games.AsNoTracking().ToList();
                    return importer.PreviewImport(SessionTextParser.Parse(importText, games, null));
                });
                if (_work.IsDisposed) return;

                ImportResultModal.ShowPreview(importResult);
                ImportResultContainer.Visibility = Visibility.Visible;
            } catch (Exception ex)
            {
                if (_work.IsDisposed) return;
                Debug.WriteLine(ex.Message);
                ImportResultModal.ShowError($"Unable to parse the imported data: {ex.Message}");
                ImportResultContainer.Visibility = Visibility.Visible;
            }
        }

        private void SummaryBackButton_Click()
        {
            ImportInputPanel.Visibility = Visibility.Visible;
            ImportResultContainer.Visibility = Visibility.Collapsed;
            ImportTextBox.Focus();
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
                    ImportResultModal.ShowSuccess($"Successfully imported {importResult!.InsertedCount + importResult.UpdatedCount} sessions.");
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
