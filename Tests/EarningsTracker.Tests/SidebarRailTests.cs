using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using FluentIcons.Wpf;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class SidebarRailTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 600, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task RailAndExpandedNavigationKeepLabelsRoutesAndActivePage(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        SeedDashboard(database);
        var day = new DateTime(2026, 11, 23);
        var dashboard = new DashboardView(database.Options);
        var created = new List<NavigationPage>();
        var window = new MainWindow(page =>
        {
            created.Add(page);
            return page == NavigationPage.Dashboard ? dashboard : new UserControl { Tag = page };
        }) { Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        var content = (ContentControl)window.FindName("MainContent");
        try
        {
            window.Show();
            await Settle();
            await dashboard.LoadDashboardData(asOfDate: day);
            ((ComboBox)dashboard.FindName("PeriodSelector")).SelectedIndex = 1;
            foreach (var row in dashboard.RecentSessions) _ = row.Cover;
            for (var attempt = 0; attempt < 40 && dashboard.RecentSessions.Any(row => row.Cover == null); attempt++)
                await Task.Delay(50);
            Assert.Equal(86m, dashboard.Balance);
            var sidebar = (FrameworkElement)window.FindName("NavigationSidebar");
            var toggle = (Button)window.FindName("ToggleNavigationButton");
            var nav = (ListBox)window.FindName("NavBar");
            var settings = (ListBox)window.FindName("SettingsNav");
            var items = nav.Items.Cast<ListBoxItem>().Concat(settings.Items.Cast<ListBoxItem>()).ToArray();
            Assert.Equal(Enum.GetValues<NavigationPage>().Order(), items.Select(item => (NavigationPage)item.Tag).Order());
            foreach (var expanded in new[] { false, true, false })
            {
                if (window.IsNavigationExpanded != expanded)
                {
                    Assert.True(toggle.Focus());
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                await Settle();
                Assert.Same(dashboard, content.Content);
                Assert.Single(created);
                Assert.Equal(expanded ? 224 : 116, sidebar.ActualWidth);
                Assert.Equal(expanded ? "Collapse navigation" : "Expand navigation", System.Windows.Automation.AutomationProperties.GetName(toggle));
                Assert.Equal(expanded, ((FrameworkElement)window.FindName("NavigationWordmark")).IsVisible);
                foreach (var item in items)
                {
                    var icon = (SymbolIcon)item.Template.FindName("NavigationIcon", item);
                    var label = Descendants<TextBlock>(item).Single(text => text.Text == (string)item.Content);
                    Assert.True(label.IsVisible);
                    Assert.True(label.ActualWidth > 0);
                    Assert.True(Bounds(label, item).Right <= item.ActualWidth);
                    Assert.True(expanded ? Bounds(icon, item).Right < Bounds(label, item).Left
                        : Bounds(icon, item).Bottom < Bounds(label, item).Top);
                    Assert.Equal(item.Content, System.Windows.Automation.AutomationProperties.GetName(item));
                    Assert.False(icon.Focusable);
                    Assert.True(Bounds(item, sidebar).Bottom <= sidebar.ActualHeight);
                }
                Assert.True(Bounds(items[4], sidebar).Bottom <= Bounds(settings, sidebar).Top);
                Capture((FrameworkElement)window.Content, $"{(expanded ? "expanded" : "rail")}-{width}x{height}-{scale:0.0}", scale);
            }
            foreach (var item in items)
            {
                var list = Equals(item.Tag, NavigationPage.Settings) ? settings : nav;
                list.SelectedItem = item;
                await Settle();
                if (!Equals(item.Tag, NavigationPage.Dashboard))
                    Assert.Equal(item.Tag, ((UserControl)content.Content).Tag);
                Assert.Same(item, list.SelectedItem);
                Assert.Null((list == nav ? settings : nav).SelectedItem);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(1000)]
    [InlineData(1360)]
    public Task ExpansionRetainsSessionDraftAcrossDrawerReflowAndModalGuards(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Synthetic sidebar draft", RecordedDate = new DateTime(2026, 11, 23),
                Date = new DateTime(2026, 11, 23), Earnings = 1m, Duration = TimeSpan.FromMinutes(30), Status = SessionStatus.Pending });
            db.SaveChanges();
        }
        var sessions = new SessionsView(database.Options);
        var window = new MainWindow(page => page == NavigationPage.Sessions ? sessions : new UserControl { Tag = page })
        { Width = width, Height = 800, Left = -10000, Top = -10000, ShowInTaskbar = false };
        SessionItemViewModel? selected = null;
        try
        {
            window.Show(); window.NavigateTo(NavigationPage.Sessions);
            await sessions.RefreshSessionsAsync();
            selected = Assert.Single(((SessionsViewModel)sessions.DataContext).Sessions);
            sessions.SelectSession(selected); await sessions.EvidenceCompletion;
            selected.BeginEdit(); selected.DraftEarnings = "9.8765";
            var toggle = (Button)window.FindName("ToggleNavigationButton");
            foreach (var expanded in new[] { true, false })
            {
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                Assert.Equal(expanded, window.IsNavigationExpanded);
                Assert.Same(sessions, ((ContentControl)window.FindName("MainContent")).Content);
                Assert.True(sessions.HasActiveDraft);
                Assert.Equal("9.8765", selected.DraftEarnings);
                var details = (Border)sessions.FindName("DetailsPanel");
                Assert.Equal(width == 1000 || expanded, ReferenceEquals(details.Parent, sessions.FindName("DrawerLayer")));
            }
            window.NavigateTo(NavigationPage.Settings);
            Assert.Same(sessions, ((ContentControl)window.FindName("MainContent")).Content);
            Assert.Equal(NavigationPage.Sessions, ((ListBoxItem)((ListBox)window.FindName("NavBar")).SelectedItem).Tag);
            selected.CancelEdit();
            var modal = (EarningsTracker.Controls.Modal)window.FindName("MainModal");
            modal.Open("Synthetic dialog", new UserControl());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(toggle.IsEnabled);
            window.NavigateTo(NavigationPage.Settings);
            Assert.Same(sessions, ((ContentControl)window.FindName("MainContent")).Content);
            modal.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(toggle.IsEnabled);
            using var read = database.CreateContext();
            Assert.Equal(1m, read.Sessions.Single().Earnings);
            Assert.Empty(read.SessionChanges);
        }
        finally { selected?.CancelEdit(); window.Close(); }
    });

    private static void SeedDashboard(TestDatabase database)
    {
        using var db = database.CreateContext();
        var day = new DateTime(2026, 11, 23);
        var games = new[] { "Predecessor", "MAVRIX", "Wreckreation" }.Select(name =>
            new Game { Name = name, RatePerHour = 2, MinutesCap = 3000 }).ToArray();
        db.Games.AddRange(games);
        var paid = new Payout { Date = day.AddDays(-17), Type = "Payment", Status = "Paid", Amount = -120 };
        db.Payouts.AddRange(paid,
            new Payout { Date = day.AddDays(-38), Type = "Payment", Status = "Paid", Amount = -786 },
            new Payout { Date = day.AddDays(-30), Type = "Payment", Status = "Processing", Amount = -50 });
        db.SaveChanges();
        for (var index = 0; index < 30; index++)
        {
            var earned = index == 0 ? 12m : index == 29 ? 16m : 14m;
            var recorded = day.AddDays(index - 29).AddHours(14);
            db.Sessions.Add(new Session
            {
                Game = games[index % games.Length], GameName = games[index % games.Length].Name,
                RecordedDate = recorded, Date = recorded, PostedDate = recorded, Earnings = earned,
                Duration = TimeSpan.FromHours((double)(earned / 2)), Status = SessionStatus.Verified,
                HasExplicitPayoutAssignment = true, AssignedPayoutId = index < 24 ? paid.Id : null
            });
        }
        db.Sessions.AddRange(
            new Session { Game = games[2], GameName = games[2].Name, RecordedDate = day.AddHours(18), Date = day.AddHours(18),
                Status = SessionStatus.Pending, Earnings = 24, Duration = TimeSpan.FromHours(12) },
            new Session { Game = games[0], GameName = games[0].Name, RecordedDate = day.AddDays(-40), Date = day.AddDays(-40),
                Status = SessionStatus.Verified, Earnings = 8, Duration = TimeSpan.FromHours(4) });
        db.SaveChanges();
        CatalogService.ImportBundled(db);
    }

    private static Rect Bounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SidebarRailVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
}
