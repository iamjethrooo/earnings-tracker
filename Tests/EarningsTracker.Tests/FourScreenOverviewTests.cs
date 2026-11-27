using System.ComponentModel;
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

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class FourScreenOverviewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 720, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task RefinedScreensPreserveNavigationEvidenceAndRecordActionGuards(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        MainWindow? window = null;
        void OpenGame(Game game)
        {
            window!.NavigateTo(NavigationPage.Games);
            window.OpenGameDetails(game);
        }
        UserControl CreatePage(NavigationPage page)
        {
            if (page == NavigationPage.Sessions)
            {
                var sessions = new SessionsView(database.Options);
                sessions.ViewGameRequested += (_, game) => OpenGame(game);
                return sessions;
            }
            return page switch
            {
                NavigationPage.Games => new GamesView(database.Options),
                NavigationPage.Payments => new PayoutsView(database.Options),
                _ => new UserControl()
            };
        }
        window = new MainWindow(CreatePage)
        {
            Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false
        };
        var content = (ContentControl)window.FindName("MainContent");
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        async Task Capture(string name)
        {
            await Settle();
            Save((FrameworkElement)window.Content, $"{name}-{width}x{height}-{scale:0.0}", scale);
        }
        try
        {
            window.Show();
            window.NavigateTo(NavigationPage.Sessions);
            await Settle();
            var sessions = Assert.IsType<SessionsView>(content.Content);
            await sessions.RefreshSessionsAsync();
            var sessionModel = (SessionsViewModel)sessions.DataContext;
            var selected = sessionModel.Sessions.Single(row => row.Session.GameName == "Predecessor" && row.Session.Earnings == 4.5m);
            sessions.SelectSession(selected);
            await sessions.EvidenceCompletion;
            await Capture("sessions");
            var sessionActions = (Button)sessions.FindName("SessionActionsButton");
            sessionActions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(sessionActions.ContextMenu.IsOpen);
            Assert.Same(selected, sessionActions.ContextMenu.DataContext);
            sessionActions.ContextMenu.IsOpen = false;
            Assert.True(VirtualizingPanel.GetIsVirtualizing((ListView)sessions.FindName("SessionsList")));
            Descendants<Button>(sessions).Single(button => Equals(button.Content, "Open game details →"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();

            var games = Assert.IsType<GamesView>(content.Content);
            Assert.Equal(selected.Session.GameId, games.SelectedGame!.Id);
            var history = (ListBox)games.FindName("GameSessionsList");
            var details = Assert.IsType<GameDetailsViewModel>(((FrameworkElement)games.FindName("GameDetailsView")).DataContext);
            Assert.Equal(36m, details.TotalEarnings);
            Assert.Equal("18h 0m / 50h", details.CycleUsageText);
            Assert.Equal("32h 0m left to earn", details.RemainingDurationText);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(history));
            Assert.Contains(Descendants<TextBlock>(games), text => text.IsVisible && text.Text.Contains("availability may have changed"));
            await Capture("game-details");
            Descendants<Button>(games).Single(button => Equals(button.Content, "Session history ↓"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Capture("game-history");
            Assert.True(((ItemsControl)games.FindName("SessionFilterList")).IsKeyboardFocusWithin);

            ((Button)games.FindName("DetailsBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            games = Assert.IsType<GamesView>(content.Content);
            var library = (GamesViewModel)games.DataContext;
            library.SortGames("Game.Id", ListSortDirection.Ascending);
            foreach (var game in library.Games) _ = game.Cover;
            var illustrated = library.Games.Where(game => !string.IsNullOrEmpty(game.Metadata?.CoverUrl)).ToList();
            for (var attempt = 0; attempt < 40 && illustrated.Any(game => game.Cover == null); attempt++)
                await Task.Delay(50);
            Assert.All(illustrated, game => Assert.NotNull(game.Cover));
            await Capture("games");
            var filters = (Button)games.FindName("MoreFiltersButton");
            filters.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            library.InstalledOnly = true;
            library.OwnedOnly = true;
            library.MultiplayerOnly = true;
            Assert.Equal(4, library.SecondaryFilterCount); // Includes the default Show archived choice.
            Assert.Equal("Filters (4)", library.MoreFiltersText);
            // Popup controls retain their bindings after moving out of the quick-filter row.
            Assert.True(((CheckBox)games.FindName("InstalledFilter")).IsChecked);
            Assert.True(((CheckBox)games.FindName("MultiplayerFilter")).IsChecked);
            library.InstalledOnly = library.OwnedOnly = library.MultiplayerOnly = false;
            ((System.Windows.Controls.Primitives.Popup)games.FindName("MoreFiltersPopup")).IsOpen = false;

            window.NavigateTo(NavigationPage.Payments);
            await Settle();
            var payments = Assert.IsType<PayoutsView>(content.Content);
            await payments.RefreshHistory();
            Assert.Equal("$1,042.00", ((TextBlock)payments.FindName("TotalEarnedValue")).Text);
            Assert.Equal("$956.00", ((TextBlock)payments.FindName("TotalPaidValue")).Text);
            Assert.Equal("$86.00", ((TextBlock)payments.FindName("AmountOwedValue")).Text);
            foreach (var name in new[] { "TotalEarnedValue", "TotalPaidValue", "AmountOwedValue" })
                Assert.Same(payments.FindResource("DefaultTextColor"), ((TextBlock)payments.FindName(name)).Foreground);
            Assert.Equal(Visibility.Visible, ((TextBlock)payments.FindName("UndatedSessionsNotice")).Visibility);
            Assert.Equal(Visibility.Visible, ((TextBlock)payments.FindName("ReconciliationNotice")).Visibility);
            var ledger = (ListView)payments.FindName("PayoutList");
            var actions = (Button)payments.FindName("PayoutActionsButton");
            ledger.SelectedItem = payments.History.Single(row => row.Payout?.Description == "Earnings correction");
            Assert.True(actions.IsEnabled);
            actions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(actions.ContextMenu.IsOpen);
            actions.ContextMenu.IsOpen = false;
            ledger.SelectedItem = payments.History.Single(row => row.Payout?.ImportedAt != null);
            Assert.True(((Button)payments.FindName("EditButton")).IsEnabled);
            Assert.False(actions.IsEnabled);
            Assert.False(((MenuItem)payments.FindName("DeleteButton")).IsEnabled);
            ledger.SelectedItem = payments.History.First(row => row.Payout == null);
            Assert.False(((Button)payments.FindName("EditButton")).IsEnabled);
            Assert.False(actions.IsEnabled);
            ledger.SelectedItem = null;
            await Capture("payments");
            Assert.True(VirtualizingPanel.GetIsVirtualizing(ledger));
            using var check = database.CreateContext();
            Assert.Empty(check.SessionChanges);
            Assert.Empty(check.PayoutChanges);
            Assert.Empty(check.GameChanges);
        }
        finally { window.Close(); }
    });

    private static void Seed(TestDatabase database)
    {
        using var db = database.CreateContext();
        var day = new DateTime(2026, 11, 22);
        string[] names = ["Predecessor", "MAVRIX", "Wreckreation", "Scooter Flow", "Bomb Rush Cyberfunk", "BioShock", "Enlisted"];
        var games = names.Select((name, index) => new Game
        {
            Name = name, RatePerHour = index < 3 ? 2 : 1.5m, MinutesCap = 3000,
            CapResetDate = new DateTime(2026, 11, 1), IsArchived = index == 6
        }).ToArray();
        db.Games.AddRange(games);
        var paid = new Payout { Date = day.AddDays(-17), Type = "Payment", Status = "Paid", Amount = -120, Description = "Payout completed" };
        db.Payouts.AddRange(paid,
            new Payout { Date = day.AddDays(-38), Type = "Payment", Status = "Paid", Amount = -786, Description = "Previous payout", ImportedAt = day.AddDays(-37) },
            new Payout { Date = day.AddDays(-30), Type = "Payment", Status = "Processing", Amount = -50, Description = "Payout processing" },
            new Payout { Date = day.AddDays(-1), Type = "Bonus", Status = "Verified", Amount = 10, Description = "Playtest bonus", ReconciliationState = "Separate" },
            new Payout { Date = day.AddDays(-2), Type = "Earnings correction", Status = "Verified", Amount = -2.5m, Description = "Earnings correction", ReconciliationState = "Separate" },
            new Payout { Date = day, Type = "Earnings correction", Status = "Verified", Amount = -1.25m, Description = "Correction awaiting reconciliation", ReconciliationState = "NeedsReview" });
        db.SaveChanges();
        void Add(int game, decimal hours, decimal earnings, int daysAgo, SessionStatus status = SessionStatus.Verified, bool settled = false)
        {
            var recorded = day.AddDays(-daysAgo).AddHours(14);
            db.Sessions.Add(new Session
            {
                GameId = games[game].Id, GameName = games[game].Name, Date = recorded, RecordedDate = recorded,
                PostedDate = status == SessionStatus.Verified ? recorded.AddHours(1) : null,
                Status = status, Duration = TimeSpan.FromHours((double)hours), Earnings = earnings,
                HasExplicitPayoutAssignment = true, AssignedPayoutId = settled ? paid.Id : null
            });
        }
        Add(0, 2.25m, 4.5m, 0); Add(0, 1.5m, 3, 1); Add(0, 1, 2, 2); Add(0, 13.25m, 26.5m, 3);
        Add(1, 12, 24, 4); Add(1, 1.5m, 3, 0, SessionStatus.Pending);
        Add(2, 4, 8, 5); Add(3, 6, 9, 6, settled: true); Add(4, 10, 15, 7); Add(6, 2, 3, 8);
        db.Sessions.AddRange(
            new Session { GameName = "Historical recordings", RecordedDate = day.AddDays(-52), Date = day.AddDays(-52),
                PostedDate = day.AddDays(-51), Status = SessionStatus.Verified, Earnings = 931.5m,
                HasExplicitPayoutAssignment = true, AssignedPayoutId = paid.Id },
            new Session { GameName = "Recording awaiting posted date", RecordedDate = day.AddDays(-51), Date = day.AddDays(-51),
                Status = SessionStatus.Verified, Earnings = 8, Duration = TimeSpan.FromHours(4), HasExplicitPayoutAssignment = true });
        db.SaveChanges();
        CatalogService.ImportBundled(db);
        db.StoreOffers.RemoveRange(db.StoreOffers);
        db.SubscriptionAvailability.RemoveRange(db.SubscriptionAvailability);
        db.SaveChanges();
        decimal[] prices = [0, 1695, 1640, 535, 950, 0, 0];
        for (var index = 0; index < games.Length; index++)
        {
            if (index == 5) continue; // Unknown acquisition must remain explicit alongside priced games.
            db.StoreOffers.Add(new StoreOffer
            {
                GameId = games[index].Id, Source = "Steam", Region = "PH", Currency = "PHP", Edition = "Base game",
                Kind = prices[index] == 0 ? AcquisitionKind.FreeToPlay : AcquisitionKind.Purchase,
                RegularPrice = prices[index], SalePrice = prices[index], VerifiedAt = day.AddDays(index == 4 ? -12 : 0),
                Provenance = "Synthetic comparison fixture"
            });
        }
        db.SubscriptionAvailability.Add(GamePassAvailabilityTests.Entry(games[0].Id, DateTime.UtcNow.AddDays(-12)));
        db.GameOwnership.Add(new GameOwnership { GameId = games[0].Id, IsOwned = true, Source = "Steam" });
        db.Bookmarks.AddRange(new Bookmark { GameId = games[0].Id }, new Bookmark { GameId = games[1].Id });
        db.SaveChanges();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Save(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "FourScreenOverviewVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
