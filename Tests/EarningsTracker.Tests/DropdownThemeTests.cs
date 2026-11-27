using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Commands;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class DropdownThemeTests(WpfTestHost host)
{
    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public Task ActionMenusHaveDarkSurfacesAndRetainNativeActions(int dpi) => host.RunAsync(async () =>
    {
        var invoked = 0;
        var button = new Button { Content = "More actions" };
        var command = new MenuItem { Header = "Set up price refresh…", InputGestureText = "Ctrl+P", Command = new RelayCommand(_ => invoked++) };
        var danger = new MenuItem { Header = "Delete session…", Foreground = (Brush)Application.Current.FindResource("DangerTextColor") };
        var disabled = new MenuItem { Header = "Unavailable action", IsEnabled = false };
        var checkedItem = new MenuItem { Header = "Include archived", IsCheckable = true, IsChecked = true };
        var submenu = new MenuItem { Header = "Other actions" };
        submenu.Items.Add(new MenuItem { Header = "Details" });
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var item in new object[] { command, danger, new Separator(), checkedItem, disabled, submenu }) menu.Items.Add(item);
        button.ContextMenu = menu;
        var window = WindowFor(button);
        try
        {
            window.Show(); menu.IsOpen = true; await Idle();
            var bitmap = Capture(menu, $"actions-{dpi}", dpi);
            AssertDarkPixel(bitmap, (int)(12 * dpi / 96d), (int)(12 * dpi / 96d));
            Assert.Same(Application.Current.FindResource("DialogBackgroundColor"), menu.Background);
            Assert.Same(danger.Foreground, Descendants<AccessText>(danger).Single().Foreground);
            var separator = menu.Items.OfType<Separator>().Single();
            var rule = Assert.IsType<Border>(VisualTreeHelper.GetChild(separator, 0));
            Assert.Same(Application.Current.FindResource("DefaultBorderColor"), rule.Background);
            Assert.False(disabled.IsEnabled);
            Assert.True(checkedItem.IsChecked);
            submenu.IsSubmenuOpen = true; await Idle();
            var popup = (Popup)submenu.Template.FindName("PART_Popup", submenu);
            Assert.True(popup.IsOpen);
            AssertDarkPixel(Capture((FrameworkElement)popup.Child, $"submenu-{dpi}", dpi), (int)(8 * dpi / 96d), (int)(8 * dpi / 96d));
            submenu.IsSubmenuOpen = false;
            var provider = (IInvokeProvider)new MenuItemAutomationPeer(command).GetPattern(PatternInterface.Invoke)!;
            provider.Invoke(); await Idle();
            Assert.Equal(1, invoked);
            Assert.False(menu.IsOpen);
        }
        finally { menu.IsOpen = false; window.Close(); }
    });

    [Theory]
    [InlineData(null)]
    [InlineData("FilterDropdown")]
    [InlineData("SearchableComboBox")]
    public Task SelectorPopupsAndChoicesStayDark(string? styleKey) => host.RunAsync(async () =>
    {
        var combo = new ComboBox { Width = 240, ItemsSource = new[] { "Current payout", "Last 30 days", "All time" }, SelectedIndex = 0 };
        if (styleKey != null) combo.Style = (Style)Application.Current.FindResource(styleKey);
        var window = WindowFor(combo);
        try
        {
            window.Show(); await Idle(); combo.IsDropDownOpen = true; await Idle();
            var border = (Border)combo.Template.FindName("DropDownBorder", combo);
            Assert.Same(Application.Current.FindResource("DialogBackgroundColor"), border.Background);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Capture((FrameworkElement)popup.Child, styleKey ?? "default-selector", 96);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.True(popup.IsOpen);
        }
        finally { combo.IsDropDownOpen = false; window.Close(); }
    });

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public Task RealPageAndEditorPopupsHaveNoLightThemeSurfaces(int dpi) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Synthetic dropdown game", RatePerHour = 2, MinutesCap = 3000 };
        using (var seed = database.CreateContext())
        {
            seed.Games.Add(game);
            seed.Sessions.Add(new Session { Game = game, GameName = game.Name, Date = DateTime.Today, RecordedDate = DateTime.Today,
                Status = SessionStatus.Pending, Duration = TimeSpan.FromMinutes(30), Earnings = 1 });
            seed.Payouts.Add(new Payout { Date = DateTime.Today, Type = "Other", Status = "Pending", Amount = -2, Description = "Synthetic dropdown record" });
            seed.SaveChanges();
        }
        var window = WindowFor(new Grid()); window.Width = 1100; window.Height = 850;
        var inspected = 0;
        async Task CheckSelectors(FrameworkElement root, string page)
        {
            await Idle(); root.UpdateLayout();
            foreach (var combo in Descendants<ComboBox>(root).Where(item => item.IsVisible && item.IsEnabled).ToList())
            {
                var selection = combo.SelectedItem;
                // Finish deferred scrolling/focus before opening the offscreen popup.
                combo.BringIntoView(); await Idle();
                combo.Focus(); await Idle();
                combo.IsDropDownOpen = true; await Idle();
                try
                {
                    var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
                    Assert.True(popup.IsOpen, $"The {page} selector {combo.Name} must be open.");
                    var bitmap = Capture((FrameworkElement)popup.Child, $"real-{page}-{inspected++}-{dpi}", dpi);
                    AssertNoLightSurface(bitmap, dpi);
                    if (combo.IsEditable)
                    {
                        var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                        Assert.NotEqual(Colors.White, ((SolidColorBrush)editor.Background).Color);
                    }
                    Assert.Same(selection, combo.SelectedItem);
                }
                finally { combo.IsDropDownOpen = false; await Idle(); }
            }
        }
        async Task CheckMenu(Button button, string page)
        {
            button.BringIntoView(); await Idle();
            Assert.True(button.IsVisible, $"The {page} action menu trigger must be visible.");
            var menu = button.ContextMenu; menu.PlacementTarget = button;
            menu.IsOpen = true; await Idle();
            try { AssertNoLightSurface(Capture(menu, $"real-{page}-actions-{dpi}", dpi), dpi); }
            finally { menu.IsOpen = false; await Idle(); }
        }
        try
        {
            window.Show();
            var dashboard = new DashboardView(database.Options); window.Content = dashboard;
            await dashboard.LoadDashboardData(); await CheckSelectors(dashboard, "dashboard");
            var sessions = new SessionsView(database.Options); window.Content = sessions;
            await sessions.RefreshSessionsAsync(); await CheckSelectors(sessions, "sessions");
            var selected = Assert.Single(((SessionsViewModel)sessions.DataContext).Sessions);
            sessions.SelectSession(selected); await sessions.EvidenceCompletion; await Idle();
            await CheckMenu((Button)sessions.FindName("SessionActionsButton"), "sessions");
            selected.BeginEdit(); await CheckSelectors(sessions, "session-editor"); selected.CancelEdit();
            var games = new GamesView(database.Options); window.Content = games;
            await ((GamesViewModel)games.DataContext).LoadGamesAsync(); await CheckSelectors(games, "games");
            Descendants<Expander>(games).Single(item => Equals(item.Header, "Library tools & status")).IsExpanded = true;
            await Idle();
            await CheckMenu((Button)games.FindName("UpdatesButton"), "games");
            ((Button)games.FindName("MoreFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            var filters = (Popup)games.FindName("MoreFiltersPopup");
            // Offscreen windows cannot retain native mouse capture across nested popups.
            // Keep the parent visible while inspecting each child's rendered surface.
            filters.StaysOpen = true;
            AssertNoLightSurface(Capture((FrameworkElement)filters.Child, $"real-game-filters-{dpi}", dpi), dpi);
            await CheckSelectors((FrameworkElement)filters.Child, "game-filters"); filters.IsOpen = false;
            games.OpenGameDetails(game);
            ((Expander)games.FindName("PersonalRatings")).IsExpanded = true;
            await CheckSelectors(games, "game-details");
            var payouts = new PayoutsView(database.Options); window.Content = payouts; await payouts.RefreshCompletion; await Idle();
            ((ListView)payouts.FindName("PayoutList")).SelectedIndex = 0; await Idle();
            await CheckMenu((Button)payouts.FindName("PayoutActionsButton"), "payments");
            var statistics = new StatisticsView(database.Options); window.Content = statistics;
            await statistics.LoadStatistics(); await CheckSelectors(statistics, "statistics");
            var settings = new SettingsView(database.Options, catalogKeyConfigured: static () => false); window.Content = settings;
            SettingsOverviewTests.SelectCategory(settings, "GamesDisplaySection"); await CheckSelectors(settings, "settings");
            using var payoutEditor = new PayoutEditView(null, database.Options); window.Content = payoutEditor;
            await CheckSelectors(payoutEditor, "payout-editor");
            using var import = new ImportSessionsView(database.Options); window.Content = import;
            ((Grid)import.FindName("ImportInputPanel")).Visibility = Visibility.Collapsed;
            ((Grid)import.FindName("ImportResultContainer")).Visibility = Visibility.Visible;
            var preview = new EarningsTracker.Utilities.SessionImporter(database.Options).PreviewImport([
                new Session { GameId = game.Id, GameName = game.Name, Date = DateTime.Today.AddDays(-1), RecordedDate = DateTime.Today.AddDays(-1), Duration = TimeSpan.FromMinutes(30), Earnings = 1, Status = SessionStatus.Verified }]);
            ((ImportResultModal)import.FindName("ImportResultModal")).ShowPreview(preview, [game]);
            await CheckSelectors(import, "import-review");
            var ownership = new GameOwnershipWindow(new GameItemViewModel(game, false, database.Options))
                { Left = -10000, Top = -10000, ShowInTaskbar = false };
            try { ownership.Show(); await CheckSelectors(ownership, "ownership"); }
            finally { ownership.Close(); }
            var installations = new LocalInstallationsWindow(new(true, [], []))
                { Left = -10000, Top = -10000, ShowInTaskbar = false };
            try { installations.Show(); await CheckSelectors(installations, "installations"); }
            finally { installations.Close(); }
            Assert.True(inspected >= 15, $"Only {inspected} page selectors were checked.");
            using var read = database.CreateContext();
            Assert.Single(read.Sessions); Assert.Single(read.Payouts);
            Assert.Empty(read.SessionChanges); Assert.Empty(read.PayoutChanges); Assert.Empty(read.GameChanges); Assert.Empty(read.GameOwnership);
        }
        finally { window.Close(); }
    });

    private static void AssertNoLightSurface(BitmapSource bitmap, int dpi)
    {
        var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        bool Light(int x, int y)
        {
            var offset = (y * width + x) * 4;
            return pixels[offset + 3] > 240 && pixels[offset] > 220 && pixels[offset + 1] > 220 && pixels[offset + 2] > 220;
        }
        var limit = (int)(40 * dpi / 96d);
        for (var y = 0; y < height; y++)
        {
            var run = 0;
            for (var x = 0; x < width; x++) { run = Light(x, y) ? run + 1 : 0; if (run >= limit) Assert.Fail($"Light horizontal surface at ({x},{y})."); }
        }
        for (var x = 0; x < width; x++)
        {
            var run = 0;
            for (var y = 0; y < height; y++) { run = Light(x, y) ? run + 1 : 0; if (run >= limit) Assert.Fail($"Light vertical surface at ({x},{y})."); }
        }
    }

    private static Window WindowFor(UIElement content) => new()
    {
        Content = new Border { Padding = new Thickness(20), Child = content }, Width = 740, Height = 550,
        Left = -10000, Top = -10000, ShowInTaskbar = false, Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor")
    };
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void AssertDarkPixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        Assert.True(pixels[0] < 100 && pixels[1] < 100 && pixels[2] < 100 && pixels[3] == 255,
            $"Expected dark popup surface; BGRA = {string.Join(',', pixels)}");
    }
    private static RenderTargetBitmap Capture(FrameworkElement element, string name, int dpi)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi / 96d), (int)Math.Ceiling(element.ActualHeight * dpi / 96d), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var directory = Path.Combine(AppContext.BaseDirectory, "DropdownThemeVisuals"); Directory.CreateDirectory(directory);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); png.Save(output);
        return bitmap;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
