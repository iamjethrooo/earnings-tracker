using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Views;
using nerve_earnings_tracker.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace nerve_earnings_tracker.Tests;

[Collection("Wpf")]
public class UxPolishTests(WpfTestHost host)
{
    [Fact]
    public Task NavigationUsesStableIdsAndSynchronizesBothLists() => host.RunAsync(() =>
    {
        var created = new List<NavigationPage>();
        var window = new MainWindow(page => { created.Add(page); return new UserControl { Tag = page }; });
        var main = (ContentControl)window.FindName("MainContent");
        var navigation = (ListBox)window.FindName("NavBar");
        var settings = (ListBox)window.FindName("SettingsNav");
        var sessions = navigation.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, NavigationPage.Sessions));
        sessions.Content = "Renamed label";
        navigation.Items.Remove(sessions);
        navigation.Items.Add(sessions);
        navigation.SelectedItem = sessions;
        Assert.Equal(NavigationPage.Sessions, ((UserControl)main.Content).Tag);
        settings.SelectedIndex = 0;
        Assert.Equal(NavigationPage.Settings, ((UserControl)main.Content).Tag);
        Assert.Null(navigation.SelectedItem);
        var current = main.Content;
        window.NavigateTo(NavigationPage.Settings);
        Assert.Same(current, main.Content);
        window.NavigateTo(NavigationPage.Payments);
        Assert.Null(settings.SelectedItem);
        Assert.Equal("Payments", Assert.IsType<ListBoxItem>(navigation.SelectedItem).Content);
        Assert.Equal(new[] { NavigationPage.Dashboard, NavigationPage.Sessions, NavigationPage.Settings, NavigationPage.Payments }, created);
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowChromeTracksProgrammaticStateChanges() => host.RunAsync(async () =>
    {
        var window = new MainWindow(_ => new UserControl()) { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            window.WindowState = WindowState.Maximized;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Visible, ((Path)window.FindName("RestoreIcon")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Path)window.FindName("MaximizeIcon")).Visibility);
            Assert.Equal(new Thickness(8), window.BorderThickness);
            window.WindowState = WindowState.Normal;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Visibility.Collapsed, ((Path)window.FindName("RestoreIcon")).Visibility);
            Assert.Equal(Visibility.Visible, ((Path)window.FindName("MaximizeIcon")).Visibility);
            Assert.Equal(new Thickness(), window.BorderThickness);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task SessionsEmptyFiltersAndFailedRefreshOfferRecoveryWithoutCoveringRows() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("EmptyImportSessionsButton")).Visibility);
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", RecordedDate = DateTime.Today, Date = DateTime.Today });
            db.SaveChanges();
        }
        await view.RefreshSessionsAsync();
        ((TextBox)view.FindName("SearchSessionsBox")).Text = "No match";
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SessionsEmptyPanel")).Visibility);
        ((Button)view.FindName("ClearSessionFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SessionsEmptyPanel")).Visibility);
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("ALTER TABLE Sessions RENAME TO SessionsUnavailable");
        await view.RefreshSessionsAsync();
        Assert.Single(((SessionsViewModel)view.DataContext).Sessions);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SessionsLoadPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SessionsEmptyPanel")).Visibility);
        Assert.Contains("Previous rows", ((TextBlock)view.FindName("SessionsLoadStatus")).Text);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)view.FindName("SessionsSummary")).Visibility);
        Capture(view, "sessions-failed-refresh");
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("ALTER TABLE SessionsUnavailable RENAME TO Sessions");
        await view.RefreshSessionsAsync();
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("SessionsLoadPanel")).Visibility);
    });
    [Fact]
    public Task GamesEmptyMessageDistinguishesNoDataFromFilteredResults() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var model = new GamesViewModel(database.Options);
        Assert.True(model.ShowEmptyState);
        Assert.Contains("No games yet", model.EmptyStateText);
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example" });
            db.SaveChanges();
        }
        await model.LoadGamesAsync();
        Assert.False(model.ShowEmptyState);
        model.SearchGames("Missing");
        Assert.True(model.ShowEmptyState);
        Assert.Contains("filters", model.EmptyStateText);
        Assert.StartsWith("0 games", model.AvailableGamesText);
        model.SearchGames(""); Assert.False(model.ShowEmptyState);
    });

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Payments")]
    [InlineData("Statistics")]
    public Task EmptyDataPagesExplainHowToGetStarted(string page) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        UserControl view = page switch
        {
            "Dashboard" => new DashboardView(database.Options),
            "Payments" => new PayoutsView(database.Options),
            _ => new StatisticsView(database.Options)
        };
        var window = new Window { Content = view, Width = 800, Height = 550, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            switch (view)
            {
                case DashboardView dashboard: await dashboard.LoadDashboardData(); break;
                case PayoutsView payments: await payments.RefreshCompletion; break;
                case StatisticsView statistics: await statistics.LoadStatistics(); break;
            }
            view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var text = (TextBlock)view.FindName(view.GetType().Name + "EmptyState");
            Assert.True(text.IsVisible);
            Assert.Contains("Import", text.Text, StringComparison.OrdinalIgnoreCase);
            Assert.True(text.ActualHeight > 0 && text.ActualWidth <= 400);
            Capture(view, page.ToLowerInvariant() + "-empty");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SettingsUsesSemanticFeedbackAndDoesNotPresentAssemblyDefaultAsRelease() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var view = new SettingsView(database.Options);
        var feedback = (TextBlock)view.FindName("Feedback");
        foreach (var pair in new[] {
            (FeedbackTone.Success, "VerifiedTextColor"),
            (FeedbackTone.Warning, "PendingTextColor"),
            (FeedbackTone.Error, "RejectedTextColor"),
            (FeedbackTone.Neutral, "SecondaryTextColor") })
        {
            view.ShowFeedback("A descriptive message", pair.Item1);
            Assert.Same(view.FindResource(pair.Item2), feedback.Foreground);
            Assert.Equal("A descriptive message", feedback.Text);
        }
        view.Measure(new Size(800, 550)); view.Arrange(new Rect(0, 0, 800, 550)); view.UpdateLayout();
        var footer = (TextBlock)view.FindName("BuildFooter");
        Assert.Equal("Development build", footer.Text);
        Assert.Contains("Assembly version", Assert.IsType<string>(footer.ToolTip));
        Assert.True(footer.ActualHeight > 0);
        Capture(view, "settings");
        Assert.Contains("AppVersion", ((SettingsViewModel)view.DataContext).Diagnostics());
        return Task.CompletedTask;
    });
    [Fact]
    public Task SettingsSaveFailureRetainsPreferenceAndShowsError() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var view = new SettingsView(database.Options);
        var automatic = (CheckBox)view.FindName("Automatic");
        automatic.IsChecked = false;
        automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var feedback = (TextBlock)view.FindName("Feedback");
        Assert.Same(view.FindResource("VerifiedTextColor"), feedback.Foreground);
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectPreferenceUpdate BEFORE UPDATE ON CatalogSettings BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        automatic.IsChecked = true;
        automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(automatic.IsChecked);
        Assert.Same(view.FindResource("RejectedTextColor"), feedback.Foreground);
        Assert.Contains("could not be saved", feedback.Text);
        Assert.False(new SettingsViewModel(database.Options).AutomaticUpdates);
        return Task.CompletedTask;
    });

    private static void Capture(UserControl view, string name)
    {
        // Render against the page canvas without starting the application or touching user data.
        view.Measure(new Size(800, 550)); view.Arrange(new Rect(0, 0, 800, 550)); view.UpdateLayout();
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle((Brush)view.FindResource("DefaultBackgroundColor"), null, new Rect(0, 0, 800, 550));
        var image = new RenderTargetBitmap(800, 550, 96, 96, PixelFormats.Pbgra32);
        image.Render(background); image.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "Phase5Visuals");
        System.IO.Directory.CreateDirectory(directory);
        using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}

