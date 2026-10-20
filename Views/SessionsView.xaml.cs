using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using EarningsTracker.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Views
{
    public partial class SessionsView : UserControl
    {
        public event EventHandler? ImportSessionsRequested;
        public event EventHandler<Game>? ViewGameRequested;
        private readonly Microsoft.EntityFrameworkCore.DbContextOptions<EarningsTracker.Data.AppDbContext>? _databaseOptions;
        private SessionsViewModel Model => (SessionsViewModel)DataContext;
        private bool _drawer;
        private bool _restoringSelection;
        private bool _refreshing;
        private SessionItemViewModel? _observedItem;
        private int _evidenceVersion;
        private Task _evidenceTask = Task.CompletedTask;
        internal Task EvidenceCompletion => _evidenceTask;
        private string _feedback = "";
        internal bool HasActiveDraft => DataContext is SessionsViewModel model &&
            (model.Sessions.Any(item => item.IsEditing || item.IsSaving) || model.SelectedSession is { IsEditing: true } or { IsSaving: true });

        internal bool CanLeave()
        {
            if (!HasActiveDraft) return true;
            ShowFeedback("Finish editing this session: Save changes or Cancel before leaving.");
            return false;
        }

        private void ShowFeedback(string message, bool canRefresh = false)
        {
            _feedback = message;
            EditorFeedback.Text = message;
            EditorFeedback.Foreground = (Brush)FindResource(canRefresh ? "PendingTextColor" : "DefaultTextColor");
            RetryEditorRefreshButton.Visibility = canRefresh ? Visibility.Visible : Visibility.Collapsed;
            PanelFeedbackText.Text = message;
            PanelFeedbackText.Foreground = EditorFeedback.Foreground;
            PanelRetryButton.Visibility = RetryEditorRefreshButton.Visibility;
            UpdatePanel();
            RevealFeedback();
        }

        public SessionsView() : this(null) { }

        internal SessionsView(Microsoft.EntityFrameworkCore.DbContextOptions<EarningsTracker.Data.AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            InitializeComponent();
            DataContext = new SessionsViewModel(databaseOptions);
            Model.PropertyChanged += Model_PropertyChanged;
            StatusFilter.SelectedIndex = 0;
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

        internal async Task RefreshSessionsAsync()
        {
            if (DataContext is not SessionsViewModel viewModel) return;
            // Finish a selected record's read before refreshing the shared snapshot.
            await _evidenceTask;
            _refreshing = true;
            try
            {
                var task = viewModel.LoadSessionsAsync();
                UpdateEmptyState(viewModel);
                await task;
            }
            finally { _refreshing = false; }
            SynchronizeSelection();
            UpdateEmptyState(viewModel);
            if (viewModel.SelectedSession != null) await StartEvidenceLoad(viewModel.SelectedSession);
        }

        private async void RetrySessions_Click(object sender, RoutedEventArgs e) => await RefreshSessionsAsync();

        private void ClearSessionFilters_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SessionsViewModel viewModel) return;
            if (HasActiveDraft) { CanLeave(); return; }
            viewModel.ClearFilters();
            StatusFilter.SelectedIndex = 0;
            UpdateEmptyState(viewModel);
        }

        private void UpdateEmptyState(SessionsViewModel viewModel)
        {
            var hasRows = viewModel.Sessions.Count > 0;
            SessionsSummary.Visibility = !viewModel.IsLoading && viewModel.LoadError == null ? Visibility.Visible : Visibility.Collapsed;
            SessionsLoadStatus.Text = viewModel.IsLoading
                ? (hasRows ? "Refreshing sessions… Previous rows remain available." : "Loading sessions…")
                : viewModel.LoadError != null
                    ? $"Could not load sessions. {(hasRows ? $"Previous rows may be out of date (loaded {viewModel.LastLoadedAt:g}). " : "")}{viewModel.LoadError}"
                    : "";
            SessionsLoadPanel.Visibility = viewModel.IsLoading || viewModel.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
            RetrySessionsButton.IsEnabled = !viewModel.IsLoading;
            SessionsEmptyState.Text = hasRows ? "No sessions match these filters." : "No sessions yet. Import your recordings to get started.";
            SessionsEmptyPanel.Visibility = !viewModel.IsLoading && viewModel.LoadError == null && viewModel.SessionsView.IsEmpty
                ? Visibility.Visible : Visibility.Collapsed;
            ClearSessionFiltersButton.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
            EmptyImportSessionsButton.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ImportSessionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (CanLeave()) ImportSessionsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EditSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: SessionItemViewModel item } && CanLeave())
            {
                SelectSession(item);
                item.BeginEdit();
                _feedback = "";
                UpdatePanel();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    DetailsScroll.ScrollToTop();
                    Descendants<DatePicker>(DetailsPanel).FirstOrDefault()?.Focus();
                }));
            }
            e.Handled = true;
        }

        private void CancelSessionEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: SessionItemViewModel item })
            {
                item.CancelEdit();
                RestoreRowFocus(item);
            }
            if (!HasActiveDraft)
            {
                SessionFilters.IsEnabled = true;
                _feedback = "";
                UpdatePanel();
            }
            e.Handled = true;
        }

        private void SaveSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: SessionItemViewModel item } || !item.TryBeginSave())
                return;
            using var updateWork = UpdateRestartGuard.Shared.Enter();
            Session? edited = null;
            string? refreshWarning = null;
            try
            {
                if (!item.TryCreateEditedSession(out edited, out var validationError) || edited == null)
                {
                    item.ShowEditError(item.InvalidField == null ? validationError : "");
                    if (item.InvalidField == null) ShowFeedback(validationError);
                    else RevealValidation(item);
                    e.Handled = true;
                    return;
                }
                SessionService.Update(item.Session.Id, edited, _databaseOptions, expectedState: item.EditState);
            }
            catch (PostCommitRefreshException ex) { refreshWarning = ex.Message; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                item.ShowEditError($"Could not save this session: {ex.Message}");
                ShowFeedback(item.EditError!);
                e.Handled = true;
                return;
            }
            finally { item.EndSave(); }

            try
            {
                item.ApplySavedSession(edited!);
                using (var db = _databaseOptions == null ? new EarningsTracker.Data.AppDbContext() : new EarningsTracker.Data.AppDbContext(_databaseOptions))
                    item.SetArtwork(item.Session.GameId is int gameId ? db.GameMetadata.Find(gameId) : null);
                if (DataContext is SessionsViewModel viewModel)
                {
                    viewModel.RefreshPresentation();
                    UpdateEmptyState(viewModel);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                refreshWarning = $"The session was saved, but the display could not be updated.\n{ex.GetBaseException().Message}";
            }
            UpdatePanel();
            ShowFeedback(refreshWarning == null ? "Session saved." :
                "The session was saved, but the display could not be refreshed. Do not save again. Retry refresh to update the display.",
                canRefresh: refreshWarning != null);
            RestoreRowFocus(item);
            e.Handled = true;
        }

        private void RevealValidation(SessionItemViewModel item)
        {
            UpdateLayout();
            var field = Descendants<Control>(DetailsPanel).FirstOrDefault(control => Equals(control.Tag, item.InvalidField));
            field?.Focus();
            (field?.Parent as FrameworkElement)?.BringIntoView();
        }

        private void RestoreRowFocus(SessionItemViewModel item)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_drawer && Model.SelectedSession != null)
                {
                    EditSessionButton.Focus();
                    return;
                }
                _restoringSelection = true;
                try
                {
                    SessionsList.ScrollIntoView(item);
                    if (SessionsList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row) row.Focus();
                    else SessionsList.Focus();
                }
                finally { _restoringSelection = false; }
            }));
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }

        private async void RetryEditorRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!CanLeave()) return;
            await RefreshSessionsAsync();
            if (DataContext is SessionsViewModel { LoadError: null })
                ShowFeedback("Sessions refreshed.");
        }

        private void DeleteSessionButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanLeave()) return;
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
                SessionService.Delete(item.Session.Id, _databaseOptions);
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

        private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SessionsViewModel.SelectedSession) ||
                e.PropertyName == nameof(SessionsViewModel.IsLoading) && !Model.IsLoading) SynchronizeSelection();
            UpdateEmptyState(Model);
            UpdatePanel();
        }

        private void SelectedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SessionItemViewModel.IsEditing) &&
                Model.SelectedSession is { IsEditing: false } selected && !Model.Sessions.Contains(selected))
            {
                Model.SelectedSession = null;
                ShowFeedback("This session is no longer in the refreshed snapshot.");
            }
            UpdatePanel();
        }

        internal void SelectSession(SessionItemViewModel? item)
        {
            if (HasActiveDraft && !ReferenceEquals(item, Model.SelectedSession)) { CanLeave(); SynchronizeSelection(); return; }
            Model.SelectedSession = item;
            SynchronizeSelection();
        }

        private void SynchronizeSelection()
        {
            var item = Model.SelectedSession;
            if (!ReferenceEquals(_observedItem, item))
            {
                if (_observedItem != null) _observedItem.PropertyChanged -= SelectedItem_PropertyChanged;
                _observedItem = item;
                if (item != null) item.PropertyChanged += SelectedItem_PropertyChanged;
                _evidenceVersion++;
                if (item != null && !_refreshing && !Model.IsLoading) _ = StartEvidenceLoad(item);
            }
            _restoringSelection = true;
            try { SessionsList.SelectedItem = Model.SessionsView.Contains(item) ? item : null; }
            finally { _restoringSelection = false; }
            UpdatePanel();
        }

        private void SessionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection || DataContext is not SessionsViewModel) return;
            if ((_refreshing || Model.IsLoading) && e.AddedItems.Count == 0) return;
            // Ignore selection events from ComboBoxes nested inside details/templates.
            if (!ReferenceEquals(e.OriginalSource, SessionsList)) return;
            SelectSession(SessionsList.SelectedItem as SessionItemViewModel);
            if (_drawer && Model.SelectedSession != null)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => CloseDetailsButton.Focus()));
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not SessionsViewModel model || StatusFilter.SelectedItem is not SessionStatusFilterItem selected) return;
            if (ReferenceEquals(selected, model.SelectedStatusFilter)) return;
            model.SelectStatus(selected);
            UpdateEmptyState(model);
        }

        private void View_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePanel();

        private void UpdatePanel()
        {
            if (DataContext is not SessionsViewModel model || DetailsPanel == null) return;
            var wasDrawer = _drawer;
            _drawer = Math.Max(0, ActualWidth - 40) < 1150;
            var hasDetails = model.SelectedSession != null;
            var host = _drawer ? DrawerLayer : Body;
            var returnFocus = DetailsPanel.IsKeyboardFocusWithin ? Keyboard.FocusedElement as UIElement : null;
            var moved = false;
            if (DetailsPanel.Parent is Grid previous && previous != host)
            {
                previous.Children.Remove(DetailsPanel);
                host.Children.Add(DetailsPanel);
                moved = true;
            }
            Grid.SetColumn(DetailsPanel, _drawer ? 0 : 2);
            DetailsColumn.Width = new GridLength(!_drawer && hasDetails ? 336 : 0);
            DetailsGap.Width = new GridLength(!_drawer && hasDetails ? 16 : 0);
            DetailsPanel.Width = _drawer ? Math.Min(360, Math.Max(280, ActualWidth - 72)) : double.NaN;
            DetailsPanel.HorizontalAlignment = _drawer ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            DetailsPanel.Visibility = hasDetails ? Visibility.Visible : Visibility.Collapsed;
            DrawerLayer.Visibility = _drawer && hasDetails ? Visibility.Visible : Visibility.Collapsed;
            BrowseContent.IsEnabled = !(_drawer && hasDetails);
            SessionFilters.IsEnabled = !HasActiveDraft;
            SessionsList.IsEnabled = !HasActiveDraft;
            KeyboardNavigation.SetTabNavigation(DetailsPanel, _drawer ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            var editing = model.SelectedSession?.IsEditing == true;
            ReadActions.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            EditActions.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            EditorFeedbackPanel.Visibility = _feedback.Length > 0 && !(_drawer && hasDetails) ? Visibility.Visible : Visibility.Collapsed;
            PanelFeedback.Visibility = _feedback.Length > 0 && _drawer && hasDetails ? Visibility.Visible : Visibility.Collapsed;
            if (moved && hasDetails)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    if (returnFocus is { IsVisible: true, IsEnabled: true }) returnFocus.Focus();
                    else if (_drawer && Model.SelectedSession != null) CloseDetailsButton.Focus();
                }));
            if (_drawer && hasDetails && !wasDrawer)
            {
                if (_feedback.Length > 0) RevealFeedback();
                else if (editing && model.SelectedSession?.InvalidField != null) RevealValidation(model.SelectedSession);
            }
        }

        private void RevealFeedback()
        {
            if (!_drawer || Model.SelectedSession == null || _feedback.Length == 0) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_drawer && Model.SelectedSession != null && PanelFeedback.Visibility == Visibility.Visible)
                    PanelFeedback.BringIntoView();
            }));
        }

        private void Table_SizeChanged(object sender, SizeChangedEventArgs e) =>
            GameColumn.Width = Math.Max(170, SessionsList.ActualWidth - 754);

        private void CloseDetails_Click(object sender, RoutedEventArgs e) => CloseDetails();
        private void CloseDetails()
        {
            if (!CanLeave()) return;
            var previous = Model.SelectedSession;
            Model.SelectedSession = null;
            SynchronizeSelection();
            if (previous != null) RestoreRowFocus(previous);
        }

        private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, DrawerLayer)) { CloseDetails(); e.Handled = true; }
        }

        private void GameLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SessionItemViewModel { Game: not null } item } && CanLeave())
                ViewGameRequested?.Invoke(this, item.Game);
            e.Handled = true;
        }

        private void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not SessionsViewModel model) return;
            // Let open dropdowns and calendars consume Escape before the enclosing drawer.
            if (e.Key == Key.Escape && (Descendants<ComboBox>(this).Any(combo => combo.IsDropDownOpen) ||
                Descendants<DatePicker>(this).Any(date => date.IsDropDownOpen))) return;
            if (e.Key == Key.Escape && model.SelectedSession is { } item)
            {
                if (item.IsSaving) { e.Handled = true; return; }
                if (item.IsEditing)
                {
                    item.CancelEdit();
                    _feedback = "";
                    UpdatePanel();
                    RestoreRowFocus(item);
                }
                else CloseDetails();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.OriginalSource is ListViewItem { DataContext: SessionItemViewModel selected })
            {
                SelectSession(selected);
                CloseDetailsButton.Focus();
                e.Handled = true;
            }
        }

        private async void RetryEvidence_Click(object sender, RoutedEventArgs e)
        {
            if (!_refreshing && !Model.IsLoading && Model.SelectedSession is { } item) await StartEvidenceLoad(item);
        }

        private Task StartEvidenceLoad(SessionItemViewModel item)
        {
            _evidenceTask = LoadEvidenceAfterAsync(_evidenceTask, item);
            return _evidenceTask;
        }

        private async Task LoadEvidenceAfterAsync(Task previous, SessionItemViewModel item)
        {
            await previous;
            if (ReferenceEquals(Model.SelectedSession, item)) await LoadEvidenceAsync(item);
        }

        private async Task LoadEvidenceAsync(SessionItemViewModel item)
        {
            var version = ++_evidenceVersion;
            EvidenceStatus.Text = "Loading recorded evidence…";
            try
            {
                var evidence = await Task.Run(async () =>
                {
                    using var db = _databaseOptions == null ? new EarningsTracker.Data.AppDbContext() : new EarningsTracker.Data.AppDbContext(_databaseOptions);
                    return await db.SessionChanges.AsNoTracking().Where(change => change.SessionId == item.Session.Id)
                        .OrderBy(change => change.ChangedAt).ThenBy(change => change.Id).ToListAsync();
                });
                if (version != _evidenceVersion || !ReferenceEquals(Model.SelectedSession, item)) return;
                item.SetEvidence(evidence);
                EvidenceStatus.Text = evidence.Count == 0 ? "No source or audit evidence was recorded for this historical session." : $"{evidence.Count} recorded audit entries. Source text and original values are read-only.";
            }
            catch (Exception ex)
            {
                if (version != _evidenceVersion || !ReferenceEquals(Model.SelectedSession, item)) return;
                System.Diagnostics.Trace.TraceError(ex.ToString());
                EvidenceStatus.Text = $"Could not refresh evidence. Previously loaded evidence may be stale. Retry evidence. {ex.GetBaseException().Message}";
            }
        }
    }
}
