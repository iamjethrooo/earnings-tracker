using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

// Audit the real Games template without App startup, provider calls, or production data.
[Collection("Wpf")]
public class GamesScrollingAuditTests(WpfTestHost host)
{
    [Theory]
    [InlineData(ScrollUnit.Item, true, 940)]
    [InlineData(ScrollUnit.Pixel, true, 940)]
    [InlineData(ScrollUnit.Pixel, false, 940)]
    [InlineData(ScrollUnit.Pixel, true, 740)]
    [InlineData(ScrollUnit.Pixel, true, 1480)]
    public Task CompareNativeScrollingAndDisabledContentScroll(ScrollUnit unit, bool contentScroll, double width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(Enumerable.Range(1, 600).Select(i => new Game
            {
                Name = $"Game {i:D3}" + (i % 5 == 0 ? " with a long complete edition and expansion title" : ""),
                RatePerHour = i % 8 + 0.25m, MinutesCap = 60 + i % 12 * 30, IsArchived = i % 10 == 0
            }));
            db.SaveChanges();
        }
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        var list = (ListBox)view.FindName("GamesList");
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
        Assert.True(ScrollViewer.GetCanContentScroll(list));
        VirtualizingPanel.SetScrollUnit(list, unit);
        ScrollViewer.SetCanContentScroll(list, contentScroll);
        var results = new List<string> { $"600 synthetic games; {unit}; CanContentScroll={contentScroll}; missing covers; {width} x 568 DIPs" };
        async Task Settle()
        {
            view.Measure(new Size(width, 568));
            view.Arrange(new Rect(0, 0, width, 568));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            view.UpdateLayout();
        }
        var timer = Stopwatch.StartNew();
        await Settle();
        results.Add($"Initial layout: {timer.Elapsed.TotalMilliseconds:F1} ms");
        var scroll = Descendants<ScrollViewer>(list).First();
        var panel = Descendants<VirtualizingStackPanel>(list).Single();
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        var originalContainers = panel.Children.Cast<ListBoxItem>().ToHashSet();
        void Observe(string stage)
        {
            var realized = panel.Children.Count;
            results.Add($"{stage}: {realized} realized, offset {scroll.VerticalOffset:F1}, viewport {scroll.ViewportHeight:F1}");
            if (contentScroll) Assert.InRange(realized, 1, 60);
            else Assert.Equal(600, realized);
        }
        Observe("Top");
        if (contentScroll && unit == ScrollUnit.Pixel)
        {
            scroll.ScrollToVerticalOffset(37.5);
            await Settle();
            Assert.Equal(37.5, scroll.VerticalOffset, 1);
            Observe("Partial row offset");
        }
        timer.Restart();
        scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2);
        await Settle();
        Observe("Middle");
        var reused = panel.Children.Cast<ListBoxItem>().Count(originalContainers.Contains);
        results.Add($"Reused initial containers in middle: {reused}");
        if (contentScroll) Assert.True(reused > 0);
        scroll.ScrollToBottom();
        await Settle();
        Observe("Bottom");
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(599));
        results.Add($"Middle + bottom layout: {timer.Elapsed.TotalMilliseconds:F1} ms");
        var rows = model.Games.ToArray();
        timer.Restart();
        foreach (var text in new[] { "G", "Ga", "Game", "Game 1", "Game 10", "" }) model.SearchGames(text);
        model.SortGames("Game.Name", ListSortDirection.Ascending);
        results.Add($"6 searches + name sort: {timer.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(rows, model.Games.ToArray());
        await Settle();
        Observe("After search/sort");
        if (contentScroll)
        {
            for (var i = 0; i < rows.Length; i++) rows[i].IsBookmarked = i % 8 == 0;
            model.FilterBookmarked(true);
            Assert.Equal(75, model.AvailableGamesCount);
            await Settle();
            scroll.ScrollToBottom();
            await Settle();
            Observe("Bookmarked filter bottom");
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(74));
            model.FilterBookmarked(false);
            model.ReloadCatalog();
            Assert.Equal(rows, model.Games.ToArray());
            await Settle();
            Observe("After catalog reload");
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, $"games-scroll-audit-{unit}-{contentScroll}-{width}.txt"), results);
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
