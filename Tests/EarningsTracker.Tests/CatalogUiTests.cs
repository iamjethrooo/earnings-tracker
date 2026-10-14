using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class CatalogUiTests(WpfTestHost host)
{
    [Fact]
    public Task OnlyGamePassCanGrantSubscriptionAccessAndPersists() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Game Pass fixture" }); db.SaveChanges();
            db.SubscriptionAvailability.Add(new SubscriptionAvailability { GameId = db.Games.Single().Id,
                Service = "PC Game Pass", Platform = "PC", Region = "PH", Included = true,
                VerifiedAt = DateTime.UtcNow, Url = "https://example.com/subscription", Provenance = "Reviewed fixture" });
            db.SaveChanges();
        }
        var model = new GamesViewModel(database.Options) { PlayableOnly = true };
        Assert.Equal(0, model.AvailableGamesCount);
        model.SaveSubscriptions(["EA Play", "Ubisoft+ Premium"]);
        Assert.Empty(model.MySubscriptions); Assert.Equal(0, model.AvailableGamesCount);
        model.SaveSubscriptions(["PC Game Pass"]); Assert.Equal(1, model.AvailableGamesCount);
        Assert.Contains("PC Game Pass", new GamesViewModel(database.Options).MySubscriptions);
        using (var db = database.CreateContext()) { db.SubscriptionAvailability.Single().VerifiedAt = DateTime.UtcNow.AddDays(-8); db.SaveChanges(); }
        model.ReloadCatalog(); Assert.Equal(0, model.AvailableGamesCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BundledGamePassMappingsPopulateFilterAndPlayableNowWithoutOwnershipChanges() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            EarningsTracker.Data.DbInitializer.Initialize(db); CatalogService.ImportBundled(db);
            Assert.NotEmpty(db.SubscriptionAvailability.Where(s => s.Service == "PC Game Pass" && s.Included));
            // Refresh dates in the isolated fixture so this check remains useful after the bundled snapshot ages.
            foreach (var subscription in db.SubscriptionAvailability) { subscription.VerifiedAt = DateTime.UtcNow; subscription.ExpiresAt = null; }
            db.SaveChanges();
        }
        var model = new GamesViewModel(database.Options) { GamePassOnly = true };
        var reviewed = model.Games.Count(x => !x.Game.IsArchived && x.Subscriptions.Any(s => s.Service == "PC Game Pass" && CatalogPresentation.Current(s, DateTime.UtcNow)));
        Assert.True(reviewed > 0);
        model.SaveSubscriptions(["PC Game Pass"]); model.PlayableOnly = true;
        Assert.True(model.AvailableGamesCount >= reviewed);
        var settings = new SettingsView(database.Options);
        Assert.True(((CheckBox)settings.FindName("GamePass")).IsChecked);
        foreach (var width in new[] { 740, 960 })
        {
            settings.Width = width; settings.Height = 560;
            settings.Measure(new Size(width, 560)); settings.Arrange(new Rect(0, 0, width, 560)); settings.UpdateLayout();
            Render(settings, $"settings-{width}.png", width, 560);
        }
        using var reopened = database.CreateContext();
        Assert.Empty(reopened.GameOwnership);
        return Task.CompletedTask;
    });

    [Fact]
    public Task GenreSelectorCombinesWithFreeAndPlayableFilters() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Free action" }, new Game { Name = "Paid strategy" }, new Game { Name = "Unknown" }); db.SaveChanges();
            var action = db.Games.Single(x => x.Name == "Free action").Id;
            var strategy = db.Games.Single(x => x.Name == "Paid strategy").Id;
            db.GameGenres.AddRange(new GameGenre { GameId = action, Name = "Action" }, new GameGenre { GameId = strategy, Name = "Strategy" });
            var free = CatalogTests.Offer(); free.GameId = action; free.SalePrice = 0;
            var paid = CatalogTests.Offer(); paid.GameId = strategy;
            db.StoreOffers.AddRange(free, paid); db.SaveChanges();
        }
        var view = new GamesView(database.Options) { Width = 1280, Height = 900 };
        view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var model = (GamesViewModel)view.DataContext;
        var selector = (ComboBox)view.FindName("GenreFilter");
        Assert.Contains("Strategy", model.GenreOptions);
        selector.SelectedItem = "Action";
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal("Action", model.SelectedGenre);
        Assert.Equal(1, model.AvailableGamesCount);
        model.FreeOnly = true; model.PlayableOnly = true;
        Assert.Equal(1, model.AvailableGamesCount);
        Assert.Equal("Free-to-play", model.Games.Single(x => x.Game.Name == "Free action").PriceText);
        selector.SelectedItem = "Strategy";
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(0, model.AvailableGamesCount);
        model.FreeOnly = false; model.PlayableOnly = false;
        Assert.Equal(1, model.AvailableGamesCount);
        selector.SelectedItem = "All genres";
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.Equal(3, model.AvailableGamesCount);
    });

    [Fact]
    public Task QuickOwnershipPreservesCostAndPopularSourcesExcludeResellers() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Active" }, new Game { Name = "Archived", IsArchived = true }); db.SaveChanges();
            var id = db.Games.First(x => x.Name == "Active").Id;
            var steam = CatalogTests.Offer(); steam.GameId = id; steam.SalePrice = 500;
            var reseller = CatalogTests.Offer(); reseller.GameId = id; reseller.Source = "Fanatical"; reseller.SalePrice = 1;
            db.StoreOffers.AddRange(steam, reseller);
            db.GameOwnership.Add(new GameOwnership { GameId = id, Source = "Steam", PurchaseAmount = 250, Currency = "PHP" });
            db.SaveChanges();
        }
        var view = new GamesView(database.Options) { Width = 1280, Height = 900 };
        var model = (GamesViewModel)view.DataContext;
        var item = model.Games.First(x => x.Game.Name == "Active");
        Assert.Equal(500, item.LowestPrice);
        Assert.DoesNotContain("Fanatical", model.StoreOptions);
        Assert.Equal("Steam", item.SourceText);
        Assert.Contains("1 unresolved", model.CatalogStatus);
        Assert.Contains("1 archived", model.CatalogStatus);
        view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
        var list = (ListBox)view.FindName("GamesList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        var button = Descendants<Button>(row).Single(x => x.Command == item.ToggleOwnedCommand);
        var invoke = (System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
        invoke.Invoke();
        await FinishOwnershipCheckAsync(view, item, database);
    });

    private static async Task FinishOwnershipCheckAsync(GamesView view, GameItemViewModel item, TestDatabase database)
    {
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert.True(item.IsOwned);
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("GameDetailsView")).Visibility);
        item.ToggleOwnedCommand.Execute(null);
        Assert.False(item.IsOwned);
        using var db = database.CreateContext();
        Assert.Equal(250, db.GameOwnership.Single().PurchaseAmount);
        Assert.Equal("Steam", db.GameOwnership.Single().Source);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    [Fact]
    public Task RestoredGameDetailsShowFullMetadataAndScrollThroughSessions() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Game game;
        using (var db = database.CreateContext())
        {
            game = new Game { Name = "Scrolling fixture", RatePerHour = 5 };
            db.Games.Add(game); db.SaveChanges();
            db.GameMetadata.Add(new GameMetadata { GameId = game.Id, CatalogId = "scroll-fixture", Reviewed = true,
                Summary = string.Join(" ", Enumerable.Repeat("A lengthy description should be readable in full without an internal scrollbar.", 35)) });
            db.Sessions.AddRange(Enumerable.Range(0, 18).Select(i => new Session { GameId = game.Id,
                GameName = game.Name, Date = DateTime.Today.AddDays(-i), RecordedDate = DateTime.Today.AddDays(-i), Duration = TimeSpan.FromMinutes(30) }));
            db.SaveChanges();
        }
        var view = new GamesView(database.Options) { Width = 740, Height = 560 };
        view.OpenGameDetails(game);
        view.Measure(new Size(740, 560)); view.Arrange(new Rect(0, 0, 740, 560)); view.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var scroll = view.DetailsScroll!;
        var metadata = (StackPanel)view.FindName("CatalogDetailsPanel");
        var sessions = (ListBox)view.FindName("GameSessionsList");
        Assert.True(metadata.ActualHeight > 220);
        Assert.Empty(Descendants<ScrollViewer>(metadata));
        Assert.True(scroll.ScrollableHeight > 0);
        Assert.Equal(19, sessions.Items.Count); // The metadata header and 18 sessions share one surface.
        // Mouse wheel over the session list must move the page rather than a nested list.
        scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
        { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        view.UpdateLayout();
        Assert.True(scroll.VerticalOffset > 0);
        scroll.ScrollToEnd(); view.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var last = (ListBoxItem)sessions.ItemContainerGenerator.ContainerFromIndex(18);
        Assert.NotNull(last);
        var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
        // Visual transforms can accumulate floating-point error at the viewport edge.
        Assert.InRange(bounds.Bottom, 0, scroll.ActualHeight + 0.000001);
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(sessions));
        Render(view, "games-restored-sessions.png", 740, 560);
        scroll.ScrollToTop(); view.UpdateLayout();
        Render(view, "games-restored-details.png", 740, 560);
    });

    [Fact]
    public Task DashboardAndSessionsRenderLinkedArtworkAndUnlinkedPlaceholder() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            EarningsTracker.Data.DbInitializer.Initialize(db); CatalogService.ImportBundled(db);
            var metadata = db.GameMetadata.First(x => x.CoverUrl != "");
            var game = db.Games.Find(metadata.GameId)!;
            db.Sessions.AddRange(new Session { GameId = game.Id, GameName = game.Name, Date = DateTime.Today, RecordedDate = DateTime.Today },
                new Session { GameName = "Unlinked session", Date = DateTime.Today, RecordedDate = DateTime.Today });
            db.SaveChanges();
        }
        var dashboard = new DashboardView(database.Options) { Width = 1280, Height = 900 };
        await dashboard.LoadDashboardData();
        var sessions = new SessionsView(database.Options) { Width = 1280, Height = 900 };
        var model = (SessionsViewModel)sessions.DataContext;
        await model.LoadSessionsAsync();
        Assert.Null(model.LoadError);
        var linked = model.Sessions.Single(x => x.Session.GameId.HasValue);
        var recent = dashboard.RecentSessions.Single(x => x.Session.GameId.HasValue);
        _ = linked.Cover; _ = recent.Cover;
        for (int i = 0; i < 40 && (linked.Cover == null || recent.Cover == null); i++) await Task.Delay(50);
        Assert.NotNull(linked.Cover); Assert.NotNull(recent.Cover);
        Assert.Null(model.Sessions.Single(x => !x.Session.GameId.HasValue).Cover);
        foreach (var (view, name) in new[] { ((FrameworkElement)dashboard, "dashboard-artwork.png"), (sessions, "sessions-artwork.png") })
        {
            view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
            Render(view, name, 1280, 900);
        }
    });

    [Fact]
    public Task BundledCatalogRendersRealArtworkOffline() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            EarningsTracker.Data.DbInitializer.Initialize(db);
            CatalogService.ImportBundled(db);
        }
        var snapshot = CatalogService.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));
        foreach (var entry in snapshot.Games.Where(x => x.CoverUrl != ""))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "Artwork", entry.CoverSha256.ToLowerInvariant() + ".img");
            Assert.True(File.Exists(path), $"Bundled artwork missing for {entry.Name}");
            Assert.Equal(entry.CoverSha256.ToUpperInvariant(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        var view = new GamesView(database.Options) { Width = 1280, Height = 900 };
        var model = (GamesViewModel)view.DataContext;
        var item = model.Games.First(x => x.Metadata?.CoverUrl.Length > 0);
        _ = item.Cover;
        for (int i = 0; i < 40 && item.Cover == null; i++) await Task.Delay(50);
        Assert.NotNull(item.Cover);
        view.Measure(new Size(1280, 900)); view.Arrange(new Rect(0, 0, 1280, 900)); view.UpdateLayout();
        await Task.Delay(300);
        Render(view, "games-populated.png", 1280, 900);
        view.OpenGameDetails(item.Game); view.UpdateLayout();
        Render(view, "games-populated-details.png", 1280, 900);
    });

    [Fact]
    public Task CombinedFiltersOwnershipAndStaleSubscriptions() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Example" }, new Game { Name = "Unknown" }); db.SaveChanges();
            var game = db.Games.First(x => x.Name == "Example");
            db.GameGenres.Add(new GameGenre { GameId = game.Id, Name = "Action" });
            var offer = CatalogTests.Offer(); offer.GameId = game.Id; db.StoreOffers.Add(offer);
            db.SubscriptionAvailability.Add(new SubscriptionAvailability { GameId = game.Id, Service = "PC Game Pass",
                Included = true, VerifiedAt = DateTime.UtcNow.AddDays(-8) }); db.SaveChanges();
        }
        var model = new GamesViewModel(database.Options);
        model.SaveSubscriptions(["PC Game Pass"]);
        model.PlayableOnly = true; Assert.Equal(0, model.AvailableGamesCount);
        using (var db = database.CreateContext()) { db.SubscriptionAvailability.Single().VerifiedAt = DateTime.UtcNow; db.SaveChanges(); }
        model.ReloadCatalog(); Assert.Equal(1, model.AvailableGamesCount);
        model.GamePassOnly = true; model.SelectedGenre = "Action"; model.SelectedStore = "Steam";
        model.SearchGames("examp"); Assert.Equal(1, model.AvailableGamesCount);
        model.FilterBookmarked(true); Assert.Equal(0, model.AvailableGamesCount);
        var item = model.Games.First(x => x.Game.Name == "Example"); item.ToggleBookmarkCommand.Execute(null);
        Assert.Equal(1, model.AvailableGamesCount);
        model.OwnedOnly = true; Assert.Equal(0, model.AvailableGamesCount);
        item.SaveOwnership(true, "Steam", 100, "PHP"); Assert.Equal(1, model.AvailableGamesCount);
        Assert.True(new GamesViewModel(database.Options).Games.First(x => x.Game.Name == "Example").IsOwned);
        model.FreeOnly = true; Assert.Equal(0, model.AvailableGamesCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task LowestPriceSortPlacesUnknownLast() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Paid" }, new Game { Name = "Unknown" }); db.SaveChanges();
            var offer = CatalogTests.Offer(); offer.GameId = db.Games.First(x => x.Name == "Paid").Id;
            db.StoreOffers.Add(offer); db.SaveChanges();
        }
        var model = new GamesViewModel(database.Options);
        model.SortGames(nameof(GameItemViewModel.PriceSortValue), ListSortDirection.Ascending);
        Assert.Equal("Paid", model.GamesView.Cast<GameItemViewModel>().First().Game.Name);
        return Task.CompletedTask;
    });

    [Fact]
    public Task GamesViewRendersCatalogAtNarrowAndWideWidthsWithVirtualization() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            for (int i = 0; i < 579; i++) db.Games.Add(new Game { Name = i == 578 ? "A very long game title with remastered editions and a subtitle that should wrap gracefully" : $"Fixture game {i}", RatePerHour = 2, MinutesCap = 3000 });
            db.SaveChanges();
            var game = db.Games.OrderByDescending(x => x.Id).First();
            db.GameMetadata.Add(new GameMetadata { GameId = game.Id, CatalogId = "fixture", Reviewed = true, Summary = "Fixture description for visual layout verification.", Provenance = "Test fixture" });
            db.GameGenres.AddRange(new GameGenre { GameId = game.Id, Name = "Action" }, new GameGenre { GameId = game.Id, Name = "Adventure" });
            var offer = CatalogTests.Offer(); offer.GameId = game.Id; db.StoreOffers.Add(offer);
            db.SaveChanges();
        }
        var view = new GamesView(database.Options);
        var list = (ListBox)view.FindName("GamesList");
        Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        foreach (var width in new[] { 800, 1280 })
        {
            view.Width = width; view.Height = 900;
            view.Measure(new Size(width, 900)); view.Arrange(new Rect(0, 0, width, 900)); view.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Render(view, $"games-{width}.png", width, 900);
        }
        var model = (GamesViewModel)view.DataContext;
        view.OpenGameDetails(model.Games.Last().Game);
        view.UpdateLayout();
        Render(view, "games-details.png", 1280, 900);
        return;
    });

    private static void Render(FrameworkElement element, string name, int width, int height)
    {
        // Outputs stay in ignored test build artifacts, never the user's database.
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "CatalogVisuals"); Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
