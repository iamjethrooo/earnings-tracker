using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentIcons.Wpf;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class GamesRefinementTests(WpfTestHost host)
{
    private static void Seed(TestDatabase database)
    {
        using var db = database.CreateContext();
        db.Games.AddRange(new Game { Name = "Regular game with a very long complete edition and expansion title", RatePerHour = 1.25m, MinutesCap = 90.5m },
            new Game { Name = "Bookmarked game", RatePerHour = 2m, MinutesCap = 120m },
            new Game { Name = "Archived game with a long title", IsArchived = true, RatePerHour = 3m, MinutesCap = 180m });
        db.SaveChanges();
        var bookmarked = db.Games.Single(g => g.Name == "Bookmarked game");
        db.Bookmarks.Add(new Bookmark { GameId = bookmarked.Id });
        db.GameOwnership.Add(new GameOwnership { GameId = bookmarked.Id, IsOwned = true });
        db.SaveChanges();
    }

    [Fact]
    public Task InactiveStateChangesAndUnchangedInputsDoNotResetView() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        var original = model.Games.ToArray();
        var resets = 0;
        model.GamesView.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        model.SearchGames(""); model.FilterBookmarked(false);
        model.SelectedGenre = "All genres"; model.SelectedStore = "All stores";
        model.FreeOnly = false; model.OwnedOnly = false; model.PlayableOnly = false; model.GamePassOnly = false;
        var item = original[0];
        item.ToggleBookmarkCommand.Execute(null);
        item.ToggleOwnedCommand.Execute(null);
        Assert.Equal(0, resets);
        Assert.Equal(2, model.BookmarkCount);
        Assert.Equal(3, model.AvailableGamesCount);
        model.SearchGames("Regular");
        var afterSearch = resets;
        model.SearchGames("Regular");
        Assert.Equal(afterSearch, resets);
        Assert.Same(item, Assert.Single(model.GamesView.Cast<GameItemViewModel>()));
        Assert.Equal(original, model.Games.ToArray());
        using var read = database.CreateContext();
        Assert.True(read.GameOwnership.Find(item.Game.Id)!.IsOwned);
        Assert.Contains(read.Bookmarks, b => b.GameId == item.Game.Id);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OwnershipChangesStillUpdateActiveMembership(bool playable) => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        model.SearchGames("Regular"); model.FilterBookmarked(true);
        if (playable) model.PlayableOnly = true; else model.OwnedOnly = true;
        var item = model.Games[0];
        item.ToggleBookmarkCommand.Execute(null);
        Assert.Equal(0, model.AvailableGamesCount);
        item.ToggleOwnedCommand.Execute(null);
        Assert.Same(item, Assert.Single(model.GamesView.Cast<GameItemViewModel>()));
        item.ToggleOwnedCommand.Execute(null);
        Assert.Equal(0, model.AvailableGamesCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task InitialNewestOrderMatchesExplicitSortAndKeepsArchivedLast() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        var initial = model.GamesView.Cast<GameItemViewModel>().ToArray();
        Assert.Equal("Bookmarked game", initial[0].Game.Name);
        Assert.True(initial[^1].Game.IsArchived);
        model.SortGames("Game.Id", ListSortDirection.Descending);
        Assert.Equal(initial, model.GamesView.Cast<GameItemViewModel>().ToArray());
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(740)]
    [InlineData(940)]
    [InlineData(1480)]
    public Task RenderCardsWithReadableStatesAndLoadedArtwork(double width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var view = new GamesView(database.Options)
        {
            FontFamily = (FontFamily)Application.Current.FindResource("AppFont"),
            Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor")
        };
        view.Measure(new Size(width, 768)); view.Arrange(new Rect(0, 0, width, 768)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)view.FindName("GamesList");
        var artworkStyle = (Style)Application.Current.FindResource("ArtworkPresenter");
        // A frozen synthetic cover exercises the loaded template without cache writes/provider calls.
        var cover = BitmapSource.Create(60, 80, 96, 96, PixelFormats.Bgr32, null,
            Enumerable.Repeat(unchecked((int)0x00565EAA), 60 * 80).ToArray(), 60 * 4);
        cover.Freeze();
        foreach (var container in Descendants<ListBoxItem>(list))
        {
            var item = (GameItemViewModel)container.DataContext;
            Assert.Equal(1d, container.Opacity);
            var bookmark = Descendants<Button>(container).Single(b => b.Command == item.ToggleBookmarkCommand);
            var icon = Assert.Single(Descendants<FluentIcon>(bookmark));
            Assert.Equal(item.IsBookmarked ? "Filled" : "Regular", icon.IconVariant.ToString());
            Assert.Equal(bookmark.Foreground, icon.Foreground);
            Assert.Equal(Application.Current.FindResource(item.IsBookmarked ? "AccentTextColor" : "SecondaryTextColor"), bookmark.Foreground);
            var ownershipError = Descendants<TextBlock>(container).Single(t => t.Style == Application.Current.FindResource("InlineValidationText"));
            Assert.Equal(Visibility.Collapsed, ownershipError.Visibility);
            var title = Descendants<TextBlock>(container).Single(t => t.Text == item.Game.Name);
            Assert.True(title.ActualWidth > 0);
            if (item.Game.IsArchived)
                Assert.Equal(12d, Descendants<TextBlock>(container).Single(t => t.Text == "Archived").FontSize);
            if (item.IsBookmarked) Descendants<ContentControl>(container).Single(c => c.Style == artworkStyle).Content = cover;
        }
        view.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "GamesVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, 768, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"games-refined-{width}.png")); encoder.Save(file);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
