using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class SteamLibraryUiTests(WpfTestHost host)
{
    [Fact]
    public Task LocalInstallationEvidenceIsOptionalAndIndependentOfOwnership() => host.RunAsync(() =>
    {
        var directory = Directory.CreateTempSubdirectory("earnings-steam-view-");
        try
        {
            var folder = Path.Combine(directory.FullName, "steamapps");
            Directory.CreateDirectory(Path.Combine(folder, "common", "Example"));
            File.WriteAllText(Path.Combine(folder, "appmanifest_220.acf"), """
                "AppState"
                {
                    "appid" "220"
                    "StateFlags" "4"
                    "installdir" "Example"
                }
                """);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                var game = new Game { Name = "Example" };
                db.Games.Add(game);
                db.SaveChanges();
                db.GameMetadata.Add(new GameMetadata { GameId = game.Id, CatalogId = "example", Reviewed = true });
                db.StoreOffers.Add(new StoreOffer { GameId = game.Id, Source = "Steam",
                    Url = "https://store.steampowered.com/app/220/" });
                db.SaveChanges();
            }
            var settings = new AppSettings(database.Options);
            settings.Set(SteamInstallations.SettingsKey, JsonSerializer.Serialize(new[] { folder }));
            var view = new GamesView(database.Options);
            var model = (GamesViewModel)view.DataContext;
            var gameItem = Assert.Single(model.Games);
            Assert.False(gameItem.IsSteamOwned);
            Assert.False(gameItem.IsOwned);
            view.OpenGameDetails(gameItem.Game);
            Render(view, "steam-install-details.png", 740, 900);
            Assert.Contains(Descendants<TextBlock>(view), text =>
                text.Text == "Steam installation detected on this PC." && text.ActualHeight > 0);
            settings.Set(SteamInstallations.SettingsKey, "");
            model.ReloadCatalog();
            Assert.False(gameItem.ShowsSteamInstallation);
            return Task.CompletedTask;
        }
        finally { directory.Delete(recursive: true); }
    });

    [Fact]
    public Task SteamOwnershipFlowsThroughFiltersDetailsCostsRefreshAndUnlink() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Matched", RatePerHour = 2, MinutesCap = 60 },
                new Game { Name = "Unmatched" }, new Game { Name = "Other store" }); db.SaveChanges();
            var matched = db.Games.Single(g => g.Name == "Matched").Id;
            db.GameMetadata.Add(new GameMetadata { GameId = matched, CatalogId = "fixture", Reviewed = true });
            db.StoreOffers.Add(new StoreOffer { GameId = matched, Source = "Steam", Url = "https://store.steampowered.com/app/220/" });
            var ambiguous = db.Games.Single(g => g.Name == "Unmatched").Id;
            db.GameMetadata.Add(new GameMetadata { GameId = ambiguous, CatalogId = "conflict", Reviewed = true });
            db.StoreOffers.AddRange(
                new StoreOffer { GameId = ambiguous, Source = "Steam", Url = "https://store.steampowered.com/app/220/" },
                new StoreOffer { GameId = ambiguous, Source = "Steam", Url = "https://store.steampowered.com/app/221/" });
            db.GameOwnership.AddRange(new GameOwnership { GameId = matched, PurchaseAmount = 5.123m, Currency = "USD", Source = "GOG" },
                new GameOwnership { GameId = db.Games.Single(g => g.Name == "Other store").Id, IsOwned = true, Source = "GOG" });
            db.SaveChanges();
        }
        var handler = new SteamLibraryTests.Handler();
        using var client = new HttpClient(handler);
        var service = new SteamLibrary(database.Options, client, new SteamLibraryTests.FakeCredentials());
        await service.LinkAsync(SteamLibraryTests.SteamId, SteamLibraryTests.Key, TestContext.Current.CancellationToken);
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        model.SteamOnly = true; Assert.Equal(1, model.AvailableGamesCount);
        model.ClearFilters(); Assert.False(model.SteamOnly); Assert.Equal(3, model.AvailableGamesCount);
        model.OwnedOnly = true; Assert.Equal(1, model.AvailableGamesCount);
        model.OwnedOnly = false; model.PlayableOnly = true; Assert.Equal(1, model.AvailableGamesCount);
        Assert.Contains("1 matched · 1 unmatched", model.SteamStatus);
        Assert.Contains("1 ambiguous", model.SteamStatus);
        Assert.Contains("conflicts", model.Games.Single(g => g.Game.Name == "Unmatched").SteamEvidence);
        handler.Respond = _ => new(System.Net.HttpStatusCode.ServiceUnavailable);
        await service.RefreshAsync(true, TestContext.Current.CancellationToken);
        view = new GamesView(database.Options);
        model = (GamesViewModel)view.DataContext;
        var item = model.Games.Single(g => g.Game.Name == "Matched");
        Assert.True(item.IsSteamOwned); Assert.False(item.IsOwned);
        Assert.Equal("Mark owned", item.OwnershipButtonText);
        Assert.Equal("Steam library · saved", item.SteamBadge);
        Assert.Contains("may be out of date", item.SteamEvidence);
        Assert.True(item.ToggleOwnedCommand.CanExecute(null));
        item.ToggleOwnedCommand.Execute(null); Assert.True(item.IsOwned); Assert.True(item.IsSteamOwned);
        Assert.Contains("2.56 hours", item.BreakEvenText);
        foreach (var width in new[] { 740, 1280 }) Render(view, $"steam-games-{width}.png", width, 900);
        var list = (ListBox)view.FindName("GamesList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        var toggle = Descendants<Button>(row).Single(button => button.Command == item.ToggleOwnedCommand);
        Assert.True(toggle.IsEnabled);
        Assert.Equal("Toggle manual ownership for Matched", System.Windows.Automation.AutomationProperties.GetName(toggle));
        Assert.Contains(Descendants<TextBlock>(row), text => text.Text == "Steam library · saved" && text.ActualHeight > 0);
        view.OpenGameDetails(item.Game);
        Assert.Same(item, ((GameDetailsViewModel)((FrameworkElement)view.FindName("GameDetailsView")).DataContext).Catalog);
        Render(view, "steam-game-details.png", 740, 900);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == item.SteamEvidence && text.ActualHeight > 0);
        item.SaveOwnership(false, "GOG", 7.891m, "USD"); Assert.False(item.IsOwned);
        using (var db = database.CreateContext())
        {
            CatalogService.Import(db, new CatalogSnapshot
            {
                SchemaVersion = 2, Version = "fixture-before-withdrawal", PublishedAt = DateTime.UtcNow.AddSeconds(-1),
                Games = [new CatalogEntry { CatalogId = "fixture", Name = "Matched", Reviewed = true,
                    SteamIdentity = new("Reviewed", "2026-01-01T00:00:00Z", "Synthetic review", [new(220, "FullGame")]) }]
            });
            CatalogService.Import(db, new CatalogSnapshot
            {
                SchemaVersion = 2, Version = "fixture-schema-2", PublishedAt = DateTime.UtcNow,
                Games = [new CatalogEntry { CatalogId = "fixture", Name = "Matched", Reviewed = true,
                    SteamIdentity = new("Withdrawn", "2026-01-01T00:00:00Z", "Synthetic correction", []) }]
            });
        }
        model.ReloadCatalog();
        Assert.False(item.IsSteamOwned);
        Assert.Contains("No reviewed Steam AppID", item.SteamEvidence);
        Assert.Equal(2, handler.Calls);
        using (var db = database.CreateContext())
        {
            CatalogService.Rollback(db, CatalogService.GetRollbackVersions(db), TestContext.Current.CancellationToken);
        }
        model.ReloadCatalog();
        Assert.True(item.IsSteamOwned);
        Assert.False(item.IsOwned);
        Assert.Equal(7.891m, item.Ownership!.PurchaseAmount);
        Assert.Equal(2, handler.Calls);
        model.PlayableOnly = true;
        await service.UnlinkAsync(TestContext.Current.CancellationToken); model.ReloadCatalog();
        Assert.False(item.IsSteamOwned); Assert.False(item.IsOwned); Assert.Equal(1, model.AvailableGamesCount);
        Assert.True(item.ToggleOwnedCommand.CanExecute(null)); Assert.Equal(7.891m, item.Ownership!.PurchaseAmount);
        using var dbAfter = database.CreateContext(); Assert.Equal(3, dbAfter.Games.Count()); Assert.Empty(dbAfter.Sessions);
    });

    [Fact]
    public Task SettingsLinkAndUnlinkUseBusyGuardClearSecretAndRenderAtSupportedWidths() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using var client = new HttpClient(new SteamLibraryTests.Handler());
        var credentials = new SteamLibraryTests.FakeCredentials();
        var service = new SteamLibrary(database.Options, client, credentials);
        var view = new SettingsView(database.Options, steamLibrary: service);
        ((TextBox)view.FindName("SteamProfile")).Text = SteamLibraryTests.SteamId;
        var password = (PasswordBox)view.FindName("SteamKey"); password.Password = SteamLibraryTests.Key;
        ((Button)view.FindName("SteamLink")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("", password.Password);
        var controls = (FrameworkElement)view.FindName("SteamControls"); Assert.False(controls.IsEnabled);
        await WaitForIdle(controls);
        Assert.True(((SettingsViewModel)view.DataContext).SteamLinked);
        Assert.Contains("Steam linked", ((TextBlock)view.FindName("SteamFeedback")).Text);
        Assert.Equal(SteamLibraryTests.Key, credentials.Key);
        foreach (var width in new[] { 740, 960 })
        {
            view.Width = width; view.Height = 720;
            view.Measure(new Size(width, 720)); view.Arrange(new Rect(0, 0, width, 720)); view.UpdateLayout();
            ((FrameworkElement)view.FindName("SteamPanel")).BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap(width, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.Combine(AppContext.BaseDirectory, "SteamVisuals"); Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, $"steam-settings-{width}.png")); encoder.Save(output);
            Assert.True(((Button)view.FindName("SteamUnlink")).IsEnabled);
        }
        ((Button)view.FindName("SteamUnlink")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForIdle(controls);
        Assert.False(((SettingsViewModel)view.DataContext).SteamLinked); Assert.Null(credentials.Key);
        Assert.False(((Button)view.FindName("SteamSync")).IsEnabled);
    });

    private static async Task WaitForIdle(FrameworkElement controls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!controls.IsEnabled) await Task.Delay(10, timeout.Token);
    }

    private static void Render(FrameworkElement view, string name, int width, int height)
    {
        view.Width = width; view.Height = height;
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "SteamVisuals"); Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
