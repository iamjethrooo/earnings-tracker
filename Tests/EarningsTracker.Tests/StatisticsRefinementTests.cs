using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class StatisticsRefinementTests(WpfTestHost host)
{
    [Theory]
    [InlineData(480)]
    [InlineData(740)]
    [InlineData(1480)]
    public Task CapTableFitsItsViewport(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            var game = new Game { Name = "A long game title with complete edition and expansion", MinutesCap = 120, RatePerHour = 12.34m };
            db.Games.Add(game);
            db.Sessions.Add(new Session { Game = game, GameName = game.Name, Date = DateTime.Today, RecordedDate = DateTime.Today,
                Status = SessionStatus.Verified, Duration = TimeSpan.FromMinutes(75), Earnings = 15.425m });
            db.SaveChanges();
        }
        var view = new StatisticsView(database.Options);
        await view.LoadStatistics();
        view.Measure(new Size(width, 1100)); view.Arrange(new Rect(0, 0, width, 1100)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout();
        var list = (ListView)view.FindName("GameCapList");
        var columns = ((GridView)list.View).Columns;
        Assert.True(columns.Sum(column => column.Width) <= list.ActualWidth - 12.5);
        var scroll = Descendants<ScrollViewer>(list).First();
        Assert.Equal(0, scroll.ScrollableWidth);
        Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "StatisticsVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(list.ActualWidth), (int)Math.Ceiling(list.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(list);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(System.IO.Path.Combine(directory, $"cap-{width}.png")); encoder.Save(file);
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

    [Fact]
    public Task RangeChangesUseSnapshotAndPreserveCapRowsAndDataContext() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var today = DateTime.Today;
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", Date = today.AddDays(-40),
                RecordedDate = today.AddDays(-40), Status = SessionStatus.Verified, Earnings = 42 });
            db.SaveChanges();
        }
        var view = new StatisticsView(database.Options);
        await view.LoadStatistics();
        Assert.Empty(view.GameEarnings);
        var capResets = 0;
        view.GameCapRows.CollectionChanged += (_, _) => capResets++;
        var resets = new List<NotifyCollectionChangedAction>();
        view.GameEarnings.CollectionChanged += (_, e) => resets.Add(e.Action);
        // An unavailable table would fail any new query. Filtering must still use the complete snapshot.
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE Sessions RENAME TO UnavailableSessions");
        ((ComboBox)view.FindName("EarningsRangeComboBox")).SelectedIndex = 1;
        Assert.Equal(42m, Assert.Single(view.GameEarnings).Earnings);
        Assert.Equal(42m, view.TrendPeriods.Sum(p => p.TotalEarnings));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, resets);
        Assert.Equal(0, capResets);
        Assert.Same(view, view.DataContext);
        await view.LoadStatistics();
        Assert.Contains("Previous values", ((TextBlock)view.FindName("LoadStatusText")).Text);
        ((ComboBox)view.FindName("EarningsRangeComboBox")).SelectedIndex = 0;
        Assert.Empty(view.GameEarnings);
        Assert.Equal(Visibility.Visible, ((Border)view.FindName("LoadStatusPanel")).Visibility);
    });

    [Theory]
    [InlineData(560, true)]
    [InlineData(740, false)]
    [InlineData(1000, false)]
    [InlineData(1480, false)]
    public Task ChartsRenderReadableResponsiveEmptyAndSignedData(double width, bool empty) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        if (!empty)
        {
            using var db = database.CreateContext();
            db.Sessions.Add(new Session { GameName = "A long title including the complete edition", Date = DateTime.Today,
                RecordedDate = DateTime.Today, Status = SessionStatus.Verified, Earnings = 123456.78m });
            db.Payouts.Add(new Payout { Date = DateTime.Today.AddDays(-1), Type = "Earnings correction",
                Amount = -123456.78m, Status = "Verified", ReconciliationState = "Separate" });
            db.SaveChanges();
        }
        var view = new StatisticsView(database.Options) { FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        await view.LoadStatistics();
        view.Measure(new Size(width, 900)); view.Arrange(new Rect(0, 0, width, 900)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout();
        Assert.Equal(width < 680 ? 1 : 3, ((UniformGrid)view.FindName("MetricCards")).Columns);
        var charts = (Grid)view.FindName("ChartsGrid");
        Assert.Equal(charts.ActualWidth < 820 ? 1 : 0, Grid.GetRow((Border)view.FindName("GameEarningsPanel")));
        var canvas = (Canvas)view.FindName("EarningsTrendCanvas");
        Assert.All(canvas.Children.OfType<TextBlock>(), label => Assert.True(label.FontSize >= 12));
        if (empty)
            Assert.Contains(canvas.Children.OfType<TextBlock>(), label => label.Text.Contains("No verified sessions"));
        else
        {
            Assert.Single(canvas.Children.OfType<Polyline>());
            Assert.Contains(canvas.Children.OfType<TextBlock>(), label => label.Text.Contains("123,456.78"));
            Assert.Equal(0m, view.TrendPeriods.Sum(p => p.TotalEarnings)); // Net zero still has real activity.
            Assert.All(canvas.Children.OfType<Ellipse>().Where(e => e.ToolTip != null), marker =>
                Assert.NotNull(((ToolTip)marker.ToolTip).Style));
        }
        var children = canvas.Children.Cast<UIElement>().ToArray();
        view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(children, canvas.Children.Cast<UIElement>().ToArray());
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "StatisticsVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(System.IO.Path.Combine(directory, $"statistics-{width}-{empty}.png")); encoder.Save(file);
    });
}
