using System.IO;
using System.Net.Http;
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
            db.GameOwnership.AddRange(new GameOwnership { GameId = matched, PurchaseAmount = 5.123m, Currency = "USD", Source = "GOG" },
                new GameOwnership { GameId = db.Games.Single(g => g.Name == "Other store").Id, IsOwned = true, Source = "GOG" });
            db.SaveChanges();
        }
        using var client = new HttpClient(new SteamLibraryTests.Handler());
        var service = new SteamLibrary(database.Options, client, new SteamLibraryTests.FakeCredentials());
        await service.LinkAsync(SteamLibraryTests.SteamId, SteamLibraryTests.Key, TestContext.Current.CancellationToken);
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        model.SteamOnly = true; Assert.Equal(1, model.AvailableGamesCount);
        model.ClearFilters(); Assert.False(model.SteamOnly); Assert.Equal(3, model.AvailableGamesCount);
        model.OwnedOnly = true; Assert.Equal(2, model.AvailableGamesCount);
        model.OwnedOnly = false; model.PlayableOnly = true; Assert.Equal(2, model.AvailableGamesCount);
        var item = model.Games.Single(g => g.Game.Name == "Matched");
        Assert.True(item.IsSteamOwned); Assert.True(item.IsOwned); Assert.False(item.Ownership!.IsOwned);
        Assert.Equal("Owned · Steam", item.OwnershipButtonText);
        Assert.False(item.ToggleOwnedCommand.CanExecute(null));
        item.ToggleOwnedCommand.Execute(null); Assert.True(item.IsOwned);
        Assert.Contains("2.56 hours", item.BreakEvenText);
        foreach (var width in new[] { 740, 1280 }) Render(view, $"steam-games-{width}.png", width, 900);
        var list = (ListBox)view.FindName("GamesList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        var badge = Descendants<Button>(row).Single(button => button.Command == item.ToggleOwnedCommand);
        Assert.False(badge.IsEnabled); Assert.Equal(1d, badge.Opacity);
        Assert.Equal("Owned on Steam: Matched", System.Windows.Automation.AutomationProperties.GetName(badge));
        view.OpenGameDetails(item.Game);
        Assert.Same(item, ((GameDetailsViewModel)((FrameworkElement)view.FindName("GameDetailsView")).DataContext).Catalog);
        Render(view, "steam-game-details.png", 740, 900);
        item.SaveOwnership(false, "GOG", 7.891m, "USD"); Assert.True(item.IsOwned);
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
