using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class GamesLibraryTests(WpfTestHost host)
{
    [Theory]
    [InlineData(740, 560)]
    [InlineData(1212, 900)]
    [InlineData(1572, 940)]
    public Task CompactRowsAndDetailsKeepStaleEvidencePricesAndHistory(int width, int height) => host.RunAsync(async () =>
    {
        using var trace = new BindingTrace();
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        GamePassAvailabilityTests.Seed(database, now);
        using (var db = database.CreateContext())
        {
            db.Games.Single(game => game.Id == 12).Name = "Synthetic availability 2";
            db.GameMetadata.Add(new GameMetadata { GameId = 2, CatalogId = "synthetic-2", Reviewed = true,
                Summary = "Synthetic acquisition fixture", Provenance = "Reviewed synthetic metadata", VerifiedAt = now.AddDays(-20) });
            var usd = CatalogTests.Offer(); usd.GameId = 2; usd.Currency = "USD"; usd.SalePrice = 19.99m; usd.VerifiedAt = now.AddDays(-15);
            var deluxe = CatalogTests.Offer(); deluxe.GameId = 2; deluxe.Edition = "Deluxe edition"; deluxe.SalePrice = 899.20m;
            var foreign = CatalogTests.Offer(); foreign.GameId = 2; foreign.Source = "GOG"; foreign.Region = "US"; foreign.Currency = "EUR"; foreign.SalePrice = 7.99m;
            db.StoreOffers.AddRange(usd, deluxe, foreign);
            db.ExchangeRates.Add(new ExchangeRate { Currency = "PHP", PerUsd = 56.25m, Date = now.AddDays(-5) });
            db.SaveChanges();
        }
        GameService.SaveCapResetDate(2, DateTime.Today.AddDays(-1), database.Options);
        var view = View(database);
        var model = (GamesViewModel)view.DataContext;
        await Layout(view, width, height);
        Descendants<CheckBox>(view).Single(box => Equals(box.Content, "PC Game Pass")).IsChecked = true;
        ((TextBox)view.FindName("SearchBox")).Text = "availability 2";
        await Layout(view, width, height);
        var item = Assert.Single(model.GamesView.Cast<GameItemViewModel>());
        var list = (ListBox)view.FindName("GamesList");
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
        var warning = Descendants<TextBlock>(row).Single(text => text.Text.Contains("Game Pass availability may have changed."));
        Assert.Same(Application.Current.FindResource("PendingTextColor"), warning.Foreground);
        Assert.Contains(now.AddDays(-8).ToString("yyyy-MM-dd HH:mm"), warning.Text);
        var warningBounds = warning.TransformToAncestor(row).TransformBounds(new Rect(warning.RenderSize));
        Assert.InRange(warningBounds.Right, 0, row.ActualWidth);
        Assert.InRange(warningBounds.Bottom, 0, row.ActualHeight);
        var comparisons = Descendants<Grid>(row).Single(grid => grid.Name == "Comparisons");
        Assert.Equal(width - 40 < 1050 ? 1 : 0, Grid.GetRow(comparisons));
        Capture(view, $"games-stale-{width}");
        list.SelectedItem = item;
        Assert.Equal(Visibility.Collapsed, ((Grid)view.FindName("GameDetailsView")).Visibility);
        Descendants<Button>(row).Single(button => Equals(button.Content, item.Game.Name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, width, height);
        var details = (GameDetailsViewModel)((Grid)view.FindName("GameDetailsView")).DataContext;
        Assert.Same(item, details.Catalog);
        Assert.Equal(2, details.RateIdentities.Count);
        Assert.Single(details.CapResetHistory);
        Assert.Equal(3, item.OfferDetails.Count);
        Assert.Contains(item.OfferDetails, offer => offer.Details.Contains("Deluxe edition"));
        Assert.Contains(item.OfferDetails, offer => offer.Details.Contains("EUR 7.99") && offer.Details.Contains("not a PH offer"));
        Assert.Contains(item.OfferDetails, offer => offer.Text.Contains("US$19.99"));
        Assert.Contains(item.SubscriptionDetails, entry => entry.Text.Contains("Game Pass availability may have changed."));
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Steam · not connected");
        Assert.Equal(width - 40 < 900 ? 1 : 0, Grid.GetRow((StackPanel)view.FindName("DetailRight")));
        Assert.Equal(Visibility.Collapsed, ((Button)view.FindName("AddSessionButton")).Visibility);
        Capture(view, $"details-stale-{width}");
        if (width == 1212) Capture(view, "details-150pct", 1.5);
        view.DetailsScroll!.ScrollToBottom();
        await Layout(view, width, height);
        var back = (Button)view.FindName("DetailsBackButton");
        var backBounds = back.TransformToAncestor(view).TransformBounds(new Rect(back.RenderSize));
        Assert.InRange(backBounds.Top, 0, height);
        Assert.InRange(backBounds.Bottom, 0, height);
        Capture(view, $"details-bottom-{width}");
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, width, height);
        Assert.Equal("availability 2", ((TextBox)view.FindName("SearchBox")).Text);
        Assert.Single(model.GamesView.Cast<GameItemViewModel>());
        using var read = database.CreateContext();
        Assert.Equal(now.AddDays(-8), read.SubscriptionAvailability.Single(entry => entry.GameId == 2).VerifiedAt);
        Assert.Single(read.GameChanges);
        Assert.Empty(read.Sessions); Assert.Empty(read.SessionChanges);
    });

    [Fact]
    public Task KeyboardEntryReturnAndFilteredBookmarkKeepTheDetailsContext() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        GamePassAvailabilityTests.Seed(database, DateTime.UtcNow);
        using (var db = database.CreateContext()) { db.Bookmarks.Add(new Bookmark { GameId = 9 }); db.SaveChanges(); }
        var view = View(database);
        var window = new Window { Content = view, Width = 1200, Height = 800, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var model = (GamesViewModel)view.DataContext;
            var item = model.Games.Single(game => game.Game.Id == 9);
            var list = (ListBox)view.FindName("GamesList");
            list.ScrollIntoView(item);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            list.SelectedItem = item;
            Assert.Null(view.SelectedGame);
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
            row.Focus();
            var offset = Descendants<ScrollViewer>(list).First().VerticalOffset;
            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(9, view.SelectedGame?.Id);
            Assert.True(((Button)view.FindName("DetailsBackButton")).IsKeyboardFocused);
            ((Button)view.FindName("DetailsBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(item, list.SelectedItem);
            Assert.True(row.IsKeyboardFocusWithin);
            Assert.Equal(offset, Descendants<ScrollViewer>(list).First().VerticalOffset, 1);
            model.FilterBookmarked(true);
            view.OpenGameDetails(item.Game);
            item.ToggleBookmarkCommand.Execute(null);
            Assert.True(model.GamesView.IsEmpty);
            Assert.Same(item, ((GameDetailsViewModel)((Grid)view.FindName("GameDetailsView")).DataContext).Catalog);
            item.ToggleOwnedCommand.Execute(null);
            Assert.False(item.IsOwned);
            Assert.Equal(1234.56m, item.Ownership!.PurchaseAmount);
            ((Button)view.FindName("DetailsBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(list.SelectedItem);
            Assert.True(list.IsEnabled);
            Assert.Equal("Steam", item.Ownership.Source);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LargeLibraryRemainsBoundedAcrossReflowAndSavedCatalogFailure() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(Enumerable.Range(1, 1200).Select(id => new Game { Name = $"Synthetic game {id:D4}", RatePerHour = id % 5 + 1, MinutesCap = 120, IsArchived = id % 10 == 0 }));
            db.SaveChanges();
        }
        var view = View(database);
        var model = (GamesViewModel)view.DataContext;
        var timer = Stopwatch.StartNew();
        var counts = new List<int>();
        foreach (var width in new[] { 740, 1212, 1572 })
        {
            await Layout(view, width, 720);
            var list = (ListBox)view.FindName("GamesList");
            var scroll = Descendants<ScrollViewer>(list).First();
            scroll.ScrollToEnd();
            await Layout(view, width, 720);
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(1199));
            var realized = Descendants<ListBoxItem>(list).Count();
            Assert.InRange(realized, 1, 80);
            counts.Add(realized);
        }
        var snapshot = model.Games.ToArray();
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO HiddenMetadata");
        await model.LoadGamesAsync();
        Assert.Equal(snapshot, model.Games.ToArray());
        Assert.NotNull(model.LoadError);
        Assert.False(model.ShowEmptyState);
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE HiddenMetadata RENAME TO GameMetadata");
        await model.LoadGamesAsync();
        Assert.Null(model.LoadError);
        Assert.Equal(1200, model.Games.Count);
        timer.Stop();
        var directory = OutputDirectory();
        File.WriteAllText(Path.Combine(directory, "workload.txt"), $"1,200 synthetic games; narrow/default/wide reflow and end-scroll; realized {string.Join(", ", counts)} rows. Layout/scroll/failure/retry sample {timer.ElapsedMilliseconds} ms, excluding seed/initial load; not a production latency guarantee.");
    });

    [Fact]
    public Task SettingsAvailabilitySummaryRendersStaleSeparatelyFromRemoval() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        GamePassAvailabilityTests.Seed(database, DateTime.UtcNow);
        var view = new SettingsView(database.Options) { FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        // Apply the templates before finding the ordinary Settings disclosure.
        view.Measure(new Size(740, 560)); view.Arrange(new Rect(0, 0, 740, 560)); view.UpdateLayout();
        var expander = Descendants<Expander>(view).Single(item => Equals(item.Header, "Availability"));
        expander.IsExpanded = true;
        view.UpdateLayout();
        var text = Descendants<TextBlock>(expander).Single(item => item.Text.Contains("Stale verification does not confirm removal"));
        Assert.True(text.ActualHeight > 0);
        Assert.True(text.ActualWidth <= 340);
        Capture(view, "settings-subscription-summary");
        return Task.CompletedTask;
    });

    private static GamesView View(TestDatabase database) => new(database.Options)
    {
        FontFamily = (FontFamily)Application.Current.FindResource("AppFont"),
        Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor"), UseLayoutRounding = true
    };

    private static async Task Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static string OutputDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GamesLibraryVisuals"); Directory.CreateDirectory(path); return path;
    }

    private static void Capture(FrameworkElement element, string name, double scale = 1)
    {
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(OutputDirectory(), name + ".png")); encoder.Save(file);
    }

    private sealed class BindingTrace : IDisposable
    {
        private readonly StringWriter _output = new();
        private readonly TextWriterTraceListener _listener;
        private readonly SourceLevels _previous;
        public BindingTrace()
        {
            _listener = new(_output); _previous = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
        }
        public void Dispose()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previous;
            _listener.Flush(); var messages = _output.ToString(); _listener.Dispose(); _output.Dispose();
            Assert.True(string.IsNullOrWhiteSpace(messages), messages);
        }
    }
}
