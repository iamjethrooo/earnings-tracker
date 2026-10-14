using System.Diagnostics;
using EarningsTracker.Enums;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EarningsTracker.Models;
using EarningsTracker.Views.Modals;

namespace EarningsTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Func<NavigationPage, UserControl> _createPage;
        internal Utilities.ApplicationUpdates? ApplicationUpdates { get; private set; }
        internal bool IsEditorOpen => MainModal.Visibility == Visibility.Visible || MainModal.IsBusy
            || MainContent.Content is Views.SessionsView { DataContext: ViewModels.SessionsViewModel sessions }
                && sessions.Sessions.Any(item => item.IsEditing);

        internal void AttachApplicationUpdates(Utilities.ApplicationUpdates updates)
        {
            ApplicationUpdates = updates;
            ApplicationUpdateNotice.DataContext = updates;
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Controls.Modal))
                .AddValueChanged(MainModal, ModalVisibilityChanged);
            Closed += (_, _) =>
            {
                System.ComponentModel.DependencyPropertyDescriptor.FromProperty(VisibilityProperty, typeof(Controls.Modal))
                    .RemoveValueChanged(MainModal, ModalVisibilityChanged);
            };
        }
        private void ModalVisibilityChanged(object? sender, EventArgs e) => ApplicationUpdates?.RefreshRestartEligibility();
        private void ApplicationUpdateNotice_Click(object sender, RoutedEventArgs e)
        {
            // The quiet notice must not discard an active inline draft or import/editor dialog.
            if (!IsEditorOpen) NavigateTo(NavigationPage.Settings);
        }

        internal Action PrepareUpdateExit()
        {
            if (IsEditorOpen) throw new InvalidOperationException("Close the editing/import dialog before restarting.");
            var page = MainContent.Content;
            var enabled = IsEnabled;
            void Restore() { IsEnabled = enabled; MainContent.Content = page; }
            try
            {
                IsEnabled = false;
                MainContent.Content = null;
                MainContent.UpdateLayout();
                // Flush queued Unloaded handlers (subscriptions and credential cleanup) before arming Update.exe.
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                if (page is IDisposable disposable) disposable.Dispose();
                Trace.Flush();
                return Restore;
            }
            catch { Restore(); throw; }
        }

        public MainWindow() : this(null) { }

        internal MainWindow(Func<NavigationPage, UserControl>? pageFactory)
        {
            _createPage = pageFactory ?? CreatePage;
            InitializeComponent();
            Closing += (_, e) => { if (MainModal.IsBusy) e.Cancel = true; };

            MainModal.CloseRequested += (sender, e) =>
            {
                MainModal.Visibility = Visibility.Collapsed;
            };

            StateChanged += (_, _) => UpdateWindowChrome();
            UpdateWindowChrome();
            NavigateTo(NavigationPage.Dashboard);
        }

        private bool _isDragging;
        private Point _dragStartPoint;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragging = e.LeftButton == MouseButtonState.Pressed;
            if (e.ClickCount == 2)
            {
                _isDragging = false;
                TitleBar.ReleaseMouseCapture();
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

                return;
            }
            TitleBar.CaptureMouse();
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPosition = e.GetPosition(this);

            double horizontalChange =
                Math.Abs(currentPosition.X - _dragStartPoint.X);

            double verticalChange =
                Math.Abs(currentPosition.Y - _dragStartPoint.Y);

            if (horizontalChange < 5 && verticalChange < 5)
                return;

            if (WindowState == WindowState.Maximized)
            {
                Point mousePosition = e.GetPosition(this);

                double horizontalPercentage = mousePosition.X / ActualWidth;

                WindowState = WindowState.Normal;

                Left = mousePosition.X - (Width * horizontalPercentage);

                Top = Mouse.GetPosition(null).Y - 20;
            }

            _isDragging = false;
            TitleBar.ReleaseMouseCapture();

            // Capture release and restoring a maximized window can process a queued mouse-up.
            if (Mouse.LeftButton != MouseButtonState.Pressed)
                return;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException ex)
            {
                Trace.TraceWarning($"Window drag ended before DragMove could start: {ex.Message}");
            }
            finally
            {
                _isDragging = false;
                TitleBar.ReleaseMouseCapture();
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            TitleBar.ReleaseMouseCapture();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void UpdateWindowChrome()
        {
            var maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
            BorderThickness = new Thickness(maximized ? 8 : 0);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public void Window_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateWindowChrome();

        private void ShowModal(string title, object content)
        {
            MainModal.Open(title, content);
        }

        private void OpenModal(string title, Func<object> createContent)
        {
            try { ShowModal(title, createContent()); }
            catch (Exception ex) { ShowReadError($"Unable to open {title}", ex); }
        }

        private static void ShowReadError(string title, Exception ex)
        {
            Trace.TraceError(ex.ToString());
            MessageBox.Show($"{ex.GetBaseException().Message}\n\nReopen the page or dialog to try again.",
                title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private NavigationPage? _activePage;
        private bool _restoringNavigation;

        public void NavBar_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_restoringNavigation && NavBar.SelectedItem is ListBoxItem { Tag: NavigationPage page })
                NavigateTo(page);
        }

        private void SettingsNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_restoringNavigation && SettingsNav.SelectedItem is ListBoxItem { Tag: NavigationPage page })
                NavigateTo(page);
        }

        internal void NavigateTo(NavigationPage page)
        {
            if (_activePage == page && MainContent.Content != null)
            {
                SelectNavigation(page);
                return;
            }
            try
            {
                var view = _createPage(page);
                MainContent.Content = view;
                _activePage = page;
                SelectNavigation(page);
            }
            catch (Exception ex)
            {
                SelectNavigation(_activePage);
                ShowReadError("Unable to open page", ex);
            }
        }

        private void SelectNavigation(NavigationPage? page)
        {
            _restoringNavigation = true;
            try
            {
                foreach (var list in new[] { NavBar, SettingsNav })
                    list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, page));
            }
            finally { _restoringNavigation = false; }
        }

        private UserControl CreatePage(NavigationPage page)
        {
            switch (page)
            {
                case NavigationPage.Dashboard:
                    var dashboardView = new Views.DashboardView();

                    dashboardView.ViewAllSessionsRequested += (sender, e) =>
                    {
                        NavigateTo(NavigationPage.Sessions);
                    };

                    dashboardView.ViewGameRequested += (sender, e) =>
                    {
                        OpenGameDetails(e);
                    };

                    return dashboardView;
                case NavigationPage.Payments:
                    var payoutsView = new Views.PayoutsView();
                    payoutsView.ImportPayoutsRequested += (sender, e) =>
                    {
                        OpenModal("Import Payouts", () =>
                        {
                            var importPayoutsView = new ImportPayoutsView();
                            importPayoutsView.ImportCompleted += (s, args) => MainModal.Close();
                            return importPayoutsView;
                        });
                    };
                    payoutsView.AddPayoutRequested += (sender, e) =>
                    {
                        OpenModal("Add Payout", () =>
                        {
                            var editView = new PayoutEditView();
                            editView.Saved += (s, args) => MainModal.Close();
                            return editView;
                        });
                    };
                    payoutsView.EditPayoutRequested += (sender, payout) =>
                    {
                        OpenModal("Edit Payout", () =>
                        {
                            var editView = new PayoutEditView(payout);
                            editView.Saved += (s, args) => MainModal.Close();
                            return editView;
                        });
                    };
                    return payoutsView;
                case NavigationPage.Games:
                    return CreateGamesView();
                case NavigationPage.Sessions:
                    var sessionsView = new Views.SessionsView();

                    sessionsView.ViewGameRequested += (sender, e) =>
                    {
                        OpenGameDetails(e);
                    };

                    sessionsView.ImportSessionsRequested += (sender, e) =>
                    {
                        OpenModal("Import Sessions", () =>
                        {
                            var importSessionsView = new Views.Modals.ImportSessionsView();

                            importSessionsView.ImportCompleted += (sender, e) =>
                            {
                                MainModal.Close();
                            };

                            return importSessionsView;
                        });
                    };

                    return sessionsView;
                case NavigationPage.Statistics:
                    return new Views.StatisticsView();
                case NavigationPage.Settings:
                    return new Views.SettingsView(null, ApplicationUpdates);
                default:
                    throw new ArgumentOutOfRangeException(nameof(page));
            }
        }

        private void OpenSettings() => NavigateTo(NavigationPage.Settings);

        public void OpenGameDetails(Game game)
        {
            try
            {
                var gamesView = MainContent.Content as Views.GamesView ?? CreateGamesView();
                gamesView.OpenGameDetails(game);
                MainContent.Content = gamesView;
                _activePage = NavigationPage.Games;
                SelectNavigation(_activePage);
            }
            catch (Exception ex) { ShowReadError("Unable to open game details", ex); }
        }

        private Views.GamesView CreateGamesView()
        {
            var gamesView = new Views.GamesView();
            gamesView.SettingsRequested += (_, _) => OpenSettings();
            gamesView.AddSessionRequested += (sender, game) =>
            {
                OpenModal("Add Session", () =>
                {
                    var addSessionView = new AddSessionView(game);
                    addSessionView.AddSessionCompleted += (s, e) =>
                    {
                        gamesView.RefreshSelectedGameSessions();
                        MainModal.Close();
                    };

                    return addSessionView;
                });
            };

            return gamesView;
        }
    }
}
