using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class GameDetailsPresentationTests(WpfTestHost host)
{
    [Fact]
    public Task DetailsOpenedBeforeCatalogLoadReceiveMetadata() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Session navigation" };
        using (var db = database.CreateContext())
        {
            db.Games.Add(game); db.SaveChanges();
            db.GameMetadata.Add(new GameMetadata { GameId = game.Id, Summary = "Saved catalog details" });
            db.SaveChanges();
        }
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        model.Games.Clear(); // Session navigation can precede the first asynchronous catalog load.
        view.OpenGameDetails(game);
        var details = (GameDetailsViewModel)((Grid)view.FindName("GameDetailsView")).DataContext;
        Assert.Null(details.Catalog);
        await model.LoadGamesAsync();
        Assert.NotNull(details.Catalog);
        Assert.Equal("Saved catalog details", details.Catalog.Summary);
    });

    [Fact]
    public Task MinutesAndFiltersKeepVerifiedCycleRules() => host.RunAsync(() =>
    {
        var game = new Game { Name = "Cycle", MinutesCap = 60, RatePerHour = 5, CapResetDate = DateTime.Today };
        var model = new GameDetailsViewModel(game,
        [
            new Session { Status = SessionStatus.Verified, RecordedDate = DateTime.Today, Duration = TimeSpan.FromMinutes(75), Earnings = 6.25m },
            new Session { Status = SessionStatus.Verified, Date = DateTime.Today.AddDays(-1), Duration = TimeSpan.FromMinutes(20), Earnings = 2 },
            new Session { Status = SessionStatus.Pending, Duration = TimeSpan.FromMinutes(30), Earnings = 2.5m }
        ]);
        Assert.Equal(75, model.TotalMinutes);
        Assert.Equal(0, model.RemainingMinutes);
        Assert.Equal(125, model.ProgressPercentage);
        Assert.Equal(6.25m, model.TotalEarnings);
        Assert.Equal(0, model.RemainingEarnings);
        Assert.Contains("75 min consumed", model.MinutesDescription);
        model.FilterSessions(SessionStatus.Pending);
        Assert.Equal(1, model.FilteredSessionCount);
        Assert.Equal(2, model.StatusFilters.Single(x => x.Status == SessionStatus.Verified).Count);
        Assert.Equal(75, model.TotalMinutes); // Filtering history must not change progress.
        model.FilterSessions(SessionStatus.Rejected);
        Assert.Contains("match this status", model.SessionEmptyText);
        var empty = new GameDetailsViewModel(new Game { Name = "Empty", MinutesCap = 0 }, []);
        Assert.Equal(0, empty.ProgressPercentage);
        Assert.Contains("Add a session", empty.SessionEmptyText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RepeatedOpenRetainsSnapshotAndExplicitRefreshKeepsFilter() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Navigation", MinutesCap = 60 };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        var view = new GamesView(database.Options);
        view.OpenGameDetails(game);
        var details = (Grid)view.FindName("GameDetailsView");
        var first = (GameDetailsViewModel)details.DataContext;
        first.FilterSessions(SessionStatus.Pending);
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name, Status = SessionStatus.Pending });
            db.SaveChanges();
        }
        view.OpenGameDetails(game);
        Assert.Same(first, details.DataContext);
        Assert.Equal(0, first.SessionCount);
        view.RefreshSelectedGameSessions();
        var refreshed = (GameDetailsViewModel)details.DataContext;
        Assert.NotSame(first, refreshed);
        Assert.Equal(1, refreshed.FilteredSessionCount);
        Assert.True(refreshed.StatusFilters.Single(x => x.Status == SessionStatus.Pending).IsSelected);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(480)]
    [InlineData(740)]
    [InlineData(1100)]
    public Task LongTitleAndChipsRenderWithinViewport(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "A long game title with an extended edition and collection name", RatePerHour = 3.5m, MinutesCap = 120 };
        using (var db = database.CreateContext())
        {
            db.Games.Add(game); db.SaveChanges();
            db.Sessions.AddRange(Enumerable.Range(0, 30).Select(i => new Session
            {
                GameId = game.Id, GameName = game.Name, Date = DateTime.Today.AddDays(-i), RecordedDate = DateTime.Today.AddDays(-i),
                Duration = TimeSpan.FromMinutes(5), Earnings = 0.29m, Status = (SessionStatus)(i % 4)
            }));
            db.SaveChanges();
        }
        var view = new GamesView(database.Options) { Width = width, Height = 560 };
        view.OpenGameDetails(game);
        view.Measure(new Size(width, 560)); view.Arrange(new Rect(0, 0, width, 560)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var chips = (ItemsControl)view.FindName("SessionFilterList");
        Assert.Equal(5, chips.Items.Count);
        foreach (var chip in Descendants<RadioButton>(chips))
        {
            var bounds = chip.TransformToAncestor(view).TransformBounds(new Rect(chip.RenderSize));
            Assert.InRange(bounds.Left, 0, width);
            Assert.InRange(bounds.Right, 0, width);
        }
        var progress = Descendants<ProgressBar>(view).Single();
        Assert.InRange(progress.Value, 0, 100);
        Assert.True(progress.ActualWidth > 0);
        Render(view, $"details-{width}-top.png", width);
        var header = (ListBoxItem)((ListBox)view.FindName("GameSessionsList")).Items[0];
        view.DetailsScroll!.ScrollToVerticalOffset(Math.Max(0, header.ActualHeight - 80)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        view.DetailsScroll.ScrollToVerticalOffset(view.DetailsScroll.VerticalOffset + 180); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Render(view, $"details-{width}-sessions.png", width);
        view.DetailsScroll!.ScrollToEnd(); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Render(view, $"details-{width}-history.png", width);
        var pending = Descendants<RadioButton>(chips).Single(x => ((SessionStatusFilterItem)x.DataContext).Status == SessionStatus.Pending);
        pending.IsChecked = true;
        Assert.Equal(8, ((GameDetailsViewModel)((Grid)view.FindName("GameDetailsView")).DataContext).FilteredSessionCount);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement view, string name, int width)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "GameDetailsVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, 560, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
}
