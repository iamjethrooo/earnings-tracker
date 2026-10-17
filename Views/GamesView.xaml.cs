using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Utilities;
using EarningsTracker.Exceptions;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Views
{
    /// <summary>
    /// Interaction logic for GamesView.xaml
    /// </summary>
    public partial class GamesView : UserControl
    {
        private Game? _selectedGame;
        private readonly DbContextOptions<AppDbContext>? _databaseOptions;

        public Game? SelectedGame
        {
            get
            {
                return _selectedGame;
            }
        }

        GameDetailsViewModel? currentGame;

        public event EventHandler<Game>? AddSessionRequested;
        public event EventHandler? SettingsRequested;

        public GamesView() : this(null) { }

        internal GamesView(DbContextOptions<AppDbContext>? databaseOptions)
        {
            _databaseOptions = databaseOptions;
            InitializeComponent();

            DataContext = new GamesViewModel(databaseOptions, loadImmediately: databaseOptions != null);
            var preferences = (GamesViewModel)DataContext;
            BookmarkFilter.IsChecked = preferences.BookmarkedOnly;
            SortComboBox.SelectedIndex = preferences.DefaultSortIndex;
            ((GamesViewModel)DataContext).Games.CollectionChanged += (_, _) => UpdateSelectedCatalog();
            GameDetailsView.DataContext = null;
            if (databaseOptions == null) Loaded += GamesView_Loaded;
        }

        private void SortComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (DataContext is not GamesViewModel viewModel)
                return;

            if (sender is not ComboBox comboBox)
                return;

            viewModel.ApplySortChoice(comboBox.SelectedIndex);
        }

        private void SearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (DataContext is not GamesViewModel viewModel)
                return;

            viewModel.SearchGames(SearchBox.Text);
        }

        private void BookmarkFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GamesViewModel viewModel)
                return;
            viewModel.FilterBookmarked(true);
        }

        private void BookmarkFilter_Unchecked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GamesViewModel viewModel)
                return;
            viewModel.FilterBookmarked(false);
        }

        private void GameDetailsBackButton_Click(object sender, RoutedEventArgs e)
        {
            SessionRowsCollection.Collection = null;
            GameDetailsView.DataContext = null;
            GamesList.SelectedItem = null;
            currentGame = null;
            _selectedGame = null;

            GamesListView.Visibility = Visibility.Visible;
            GameDetailsView.Visibility = Visibility.Collapsed;
        }

        public void OpenGameDetails(Game game)
        {
            if (ReferenceEquals(_selectedGame, game) && currentGame != null)
                return;
            LoadGameDetails(game);
        }

        private void LoadGameDetails(Game game)
        {
            var offset = _selectedGame?.Id == game.Id ? DetailsScroll?.VerticalOffset ?? 0 : 0;
            using var db = _databaseOptions == null ? new AppDbContext() : new AppDbContext(_databaseOptions);

            var sessions = db.Sessions.Where(s => s.GameId == game.Id).OrderByDescending(g => g.Date).ToList();
            currentGame = new GameDetailsViewModel(game, sessions);
            currentGame.Catalog = (DataContext as GamesViewModel)?.Games.FirstOrDefault(x => x.Game.Id == game.Id);
            _selectedGame = game;
            GameDetailsView.DataContext = currentGame;
            SessionRowsCollection.Collection = currentGame.SessionsView;

            GamesListView.Visibility = Visibility.Collapsed;
            GameDetailsView.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { if (_selectedGame?.Id == game.Id) DetailsScroll?.ScrollToVerticalOffset(offset); }));
        }

        public void RefreshSelectedGameSessions()
        {
            if (_selectedGame != null)
            {
                var status = currentGame?.StatusFilters.FirstOrDefault(x => x.IsSelected)?.Status;
                LoadGameDetails(_selectedGame);
                currentGame!.FilterSessions(status);
            }
        }

        internal void UpdateSelectedCatalog()
        {
            if (currentGame != null && DataContext is GamesViewModel model)
                currentGame.Catalog = model.Games.FirstOrDefault(item => item.Game.Id == currentGame.Game.Id);
        }

        internal ScrollViewer? DetailsScroll
        {
            get
            {
                GameSessionsList.ApplyTemplate();
                return FindScrollViewer(GameSessionsList);
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindScrollViewer(child) is { } nested) return nested;
            }
            return null;
        }

        private async void GamesView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= GamesView_Loaded;
            await RefreshCatalogAsync(false);
        }

        private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) => await RefreshCatalogAsync(true);

        private async Task RefreshCatalogAsync(bool manual)
        {
            if (DataContext is not GamesViewModel model) return;
            if (!RefreshCatalogButton.IsEnabled) return;
            RefreshCatalogButton.IsEnabled = false;
            GamesList.IsEnabled = false;
            try
            {
                if (!model.HasLoaded)
                {
                    await model.LoadGamesAsync();
                    if (!model.HasLoaded) return;
                }
                var message = await new CatalogUpdates(_databaseOptions).RefreshAsync(manual);
                if (message != "") { await model.ReloadCatalogAsync(); model.SetStatus(model.CatalogStatus + " · " + message); }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Catalog refresh failed ({ex.GetType().Name}).");
                model.SetStatus("Update failed. Saved catalog and personal data are still available.");
            }
            finally { RefreshCatalogButton.IsEnabled = true; GamesList.IsEnabled = true; }
        }

        private void OpenCatalogLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string url } && CatalogService.IsHttps(url))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception) { MessageBox.Show("Unable to open this link.", "Store link"); }
            }
        }

        private void CatalogSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Ownership_Click(object sender, RoutedEventArgs e)
        {
            if (currentGame?.Catalog is not GameItemViewModel item) return;
            new Modals.GameOwnershipWindow(item) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void DetailSessionFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (currentGame != null && sender is RadioButton { DataContext: SessionStatusFilterItem filter })
                currentGame.FilterSessions(filter.Status);
        }

        private void GamesList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
        {
            if (GamesList.SelectedItem is GameItemViewModel gameItem)
            {
                try { OpenGameDetails(gameItem.Game); }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                    GamesList.SelectedItem = null;
                    MessageBox.Show($"Game details could not be loaded. Select the game again to retry.\n{ex.GetBaseException().Message}",
                        "Unable to open game details", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GamesList_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
                return;

            if (source is Button || FindParent<Button>(source) != null)
                return;

            var item = ItemsControl.ContainerFromElement(
                GamesList,
                source) as ListBoxItem;

            item?.IsSelected = true;
        }

        private static T? FindParent<T>(DependencyObject child)
    where T : DependencyObject
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(child);

            while (parent != null)
            {
                if (parent is T result)
                    return result;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        private void AddSessionButton_Click(object sender, RoutedEventArgs e)
        {
            AddSessionRequested?.Invoke(this, currentGame!.Game);
        }

        private void SaveCapResetDateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGame == null)
                return;

            var resetDate = CapResetDatePicker.SelectedDate?.Date;
            if (resetDate > DateTime.Today)
            {
                MessageBox.Show("The cap reset date cannot be in the future.", "Invalid reset date", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                GameService.SaveCapResetDate(_selectedGame.Id, resetDate, _databaseOptions);
            }
            catch (PostCommitRefreshException ex)
            {
                _selectedGame.CapResetDate = resetDate;
                MessageBox.Show(ex.Message, "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                MessageBox.Show($"The reset date was not saved.\n{ex.GetBaseException().Message}", "Unable to save reset date", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _selectedGame.CapResetDate = resetDate;
            try { RefreshSelectedGameSessions(); }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                MessageBox.Show($"The cap reset date was saved, but game details could not be refreshed. Reopen the page to reload them.\n{ex.GetBaseException().Message}",
                    "Saved — refresh failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show("Cap reset date saved.", "Game cap", MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }

}
