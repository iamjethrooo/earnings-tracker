using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using EarningsTracker.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EarningsTracker.Views
{
    public partial class SessionsView : UserControl
    {
        public event EventHandler? ImportSessionsRequested;
        public event EventHandler<Game>? ViewGameRequested;

        public SessionsView() : this(null) { }

        internal SessionsView(Microsoft.EntityFrameworkCore.DbContextOptions<EarningsTracker.Data.AppDbContext>? databaseOptions)
        {
            InitializeComponent();
            DataContext = new SessionsViewModel(databaseOptions);
            Loaded += SessionsView_Loaded;
            Unloaded += SessionsView_Unloaded;
        }

        private async void SessionsView_Loaded(object sender, RoutedEventArgs e)
        {
            PayoutService.DataChanged += PayoutService_DataChanged;
            if (DataContext is not SessionsViewModel viewModel)
                return;

            await RefreshSessionsAsync();
        }

        private void SessionsView_Unloaded(object sender, RoutedEventArgs e) =>
            PayoutService.DataChanged -= PayoutService_DataChanged;

        private async void PayoutService_DataChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => PayoutService_DataChanged(sender, e)); return; }
            if (!IsLoaded || DataContext is not SessionsViewModel viewModel)
                return;
            await RefreshSessionsAsync();
        }

        private void SearchSessionsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not SessionsViewModel viewModel)
                return;
            viewModel.SearchText = SearchSessionsBox.Text;
            UpdateEmptyState(viewModel);
        }

        private void SessionFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || DataContext is not SessionsViewModel viewModel ||
                sender is not RadioButton { DataContext: SessionStatusFilterItem item })
                return;
            viewModel.SelectStatus(item);
            UpdateEmptyState(viewModel);
        }

        internal async Task RefreshSessionsAsync()
        {
            if (DataContext is not SessionsViewModel viewModel) return;
            var task = viewModel.LoadSessionsAsync();
            UpdateEmptyState(viewModel);
            await task;
            UpdateEmptyState(viewModel);
        }

        private async void RetrySessions_Click(object sender, RoutedEventArgs e) => await RefreshSessionsAsync();

        private void ClearSessionFilters_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionsViewModel viewModel) return;
            SearchSessionsBox.Clear();
            viewModel.SelectStatus(viewModel.StatusFilters[0]);
            UpdateEmptyState(viewModel);
        }

        private void UpdateEmptyState(SessionsViewModel viewModel)
        {
            var hasRows = viewModel.Sessions.Count > 0;
            SessionsSummary.Visibility = !viewModel.IsLoading && viewModel.LoadError == null ? Visibility.Visible : Visibility.Collapsed;
            SessionsLoadStatus.Text = viewModel.IsLoading
                ? (hasRows ? "Refreshing sessions… Previous rows remain available." : "Loading sessions…")
                : viewModel.LoadError != null
                    ? $"Could not load sessions. {(hasRows ? "Previous rows may be out of date. " : "")}{viewModel.LoadError}"
                    : "";
            SessionsLoadPanel.Visibility = viewModel.IsLoading || viewModel.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
            RetrySessionsButton.IsEnabled = !viewModel.IsLoading;
            SessionsEmptyState.Text = hasRows ? "No sessions match these filters." : "No sessions yet. Import your recordings to get started.";
            SessionsEmptyPanel.Visibility = !viewModel.IsLoading && viewModel.LoadError == null && viewModel.SessionsView.IsEmpty
                ? Visibility.Visible : Visibility.Collapsed;
            ClearSessionFiltersButton.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            EmptyImportSessionsButton.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ImportSessionsButton_Click(object sender, RoutedEventArgs e) =>
            ImportSessionsRequested?.Invoke(this, EventArgs.Empty);

        private void EditSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: SessionItemViewModel item })
                item.BeginEdit();
            e.Handled = true;
        }

        private void CancelSessionEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: SessionItemViewModel item })
                item.CancelEdit();
            e.Handled = true;
        }

        private void SaveSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SessionItemViewModel item })
                return;
            Session? edited = null;
            string? refreshWarning = null;
            try
            {
                if (!item.TryCreateEditedSession(out edited, out var validationError) || edited == null)
                {
                    item.ShowEditError(validationError);
                    e.Handled = true;
                    return;
                }
                SessionService.Update(item.Session.Id, edited);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                item.ShowEditError($"Could not save this session: {ex.Message}");
                e.Handled = true;
                return;
            }

            try
            {
                item.ApplySavedSession(edited!);
                using (var db = new EarningsTracker.Data.AppDbContext())
                    item.SetArtwork(item.Session.GameId is int gameId ? db.GameMetadata.Find(gameId) : null);
                if (DataContext is SessionsViewModel viewModel)
                    UpdateEmptyState(viewModel);
            }
            catch (Exception ex)
            {
                refreshWarning = $"The session was saved, but the display could not be updated.\n{ex.GetBaseException().Message}";
            }
            if (refreshWarning != null)
                MessageBox.Show(refreshWarning, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SessionItemViewModel item })
                return;

            var result = MessageBox.Show(
                $"Delete the session for {item.Session.GameName}, recorded {item.Session.RecordedDate:d}? This removes the session from the list and keeps a deletion record in history.",
                "Delete session?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Handled = true;
                return;
            }

            try
            {
                SessionService.Delete(item.Session.Id);
            }
            catch (PostCommitRefreshException ex)
            {
                MessageBox.Show(ex.Message, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete this session: {ex.Message}", "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void SessionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SessionsList.SelectedItem is SessionItemViewModel { IsEditing: false, Game: not null } item)
                ViewGameRequested?.Invoke(this, item.Game);
        }

        private void SessionsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && e.OriginalSource is FrameworkElement { DataContext: SessionItemViewModel { IsEditing: true } editing })
            {
                editing.CancelEdit();
                if (SessionsList.ItemContainerGenerator.ContainerFromItem(editing) is ListBoxItem editingRow)
                    editingRow.Focus();
                e.Handled = true;
                return;
            }
            // Enter on an embedded editor or action button must retain its usual behavior.
            if (e.Key != Key.Enter || e.OriginalSource is not ListBoxItem row ||
                row.DataContext is not SessionItemViewModel { IsEditing: false, Game: not null } item)
                return;
            ViewGameRequested?.Invoke(this, item.Game);
            e.Handled = true;
        }
    }
}
