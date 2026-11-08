using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class DashboardRefinementTests(WpfTestHost host)
{
    [Theory]
    [InlineData("2026-06-01", 8, 8)]
    [InlineData("2026-06-07", 13, 13)]
    [InlineData("2027-01-01", 13, 8)]
    public Task DashboardMatchesStatisticsAndPublishesOneActivityReset(string date, decimal week, decimal month) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var day = DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using (var db = database.CreateContext())
        {
            db.Sessions.AddRange(Recording(day, 10), Recording(day.AddDays(-1), 5),
                Recording(day.AddDays(1), 99), Recording(day, 100, SessionStatus.Pending));
            db.Payouts.AddRange(new Payout { Date = day, Type = "Earnings correction", Status = "Verified",
                Amount = -2, ReconciliationState = "Separate" },
                new Payout { Date = day, Type = "Bonus", Status = "Verified", Amount = 100, ReconciliationState = "NeedsReview" },
                new Payout { Date = day, Type = "Payment", Status = "Processing", Amount = -4 });
            db.SaveChanges();
        }
        var dashboard = new DashboardView(database.Options);
        var notifications = new List<NotifyCollectionChangedAction>();
        dashboard.RecentSessions.CollectionChanged += (_, e) => notifications.Add(e.Action);
        await dashboard.LoadDashboardData(asOfDate: day.AddHours(4));
        var statistics = new StatisticsView(database.Options);
        await statistics.LoadStatistics(asOfDate: day.AddHours(4));
        Assert.Equal(8m, dashboard.TodayEarnings);
        Assert.Equal(week, dashboard.ThisWeekEarnings);
        Assert.Equal(month, dashboard.ThisMonthEarnings);
        Assert.Equal(statistics.TodayEarnings, dashboard.TodayEarnings);
        Assert.Equal(statistics.ThisWeekEarnings, dashboard.ThisWeekEarnings);
        Assert.Equal(statistics.ThisMonthEarnings, dashboard.ThisMonthEarnings);
        Assert.Equal(112m, dashboard.TotalEarned); // Lifetime includes the future recorded session, as before.
        Assert.Equal(4m, dashboard.TotalPaidOut);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, notifications);
        using (var db = database.CreateContext())
        {
            db.Sessions.First().Earnings = 999;
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO UnavailableMetadata");
        }
        await dashboard.LoadDashboardData(changesSaved: true, asOfDate: day);
        Assert.Equal(8m, dashboard.TodayEarnings);
        Assert.Equal(112m, dashboard.TotalEarned);
        Assert.Single(notifications);
        Assert.Contains("Changes were saved", ((TextBlock)dashboard.FindName("LoadStatusText")).Text);
    });

    [Theory]
    [InlineData(740, 420, 7)]
    [InlineData(1000, 688, 7)]
    [InlineData(1480, 688, 7)]
    [InlineData(740, 568, 0)]
    [InlineData(1000, 688, 1)]
    [InlineData(740, 568, 1)]
    public Task CardsAndRecentActivityStayWithinTheirViewports(double width, double height, int sessionCount) => host.RunAsync(async () =>
    {
        var empty = sessionCount == 0;
        using var database = new TestDatabase();
        if (!empty)
        {
            using var db = database.CreateContext();
            var game = new Game { Name = "A long game title with an expansion and complete edition" };
            db.Games.Add(game);
            for (var i = 0; i < sessionCount; i++)
            {
                var session = Recording(DateTime.Today.AddHours(i), 123456.78m);
                session.Game = game; session.GameName = game.Name; session.PostedDate = DateTime.Today;
                db.Sessions.Add(session);
            }
            db.SaveChanges();
        }
        var view = new DashboardView(database.Options) { FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        await view.LoadDashboardData();
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        Assert.Equal(5, view.Metrics.Count);
        var scroll = (ScrollViewer)view.FindName("TotalsScroll");
        Assert.Equal(0, scroll.ScrollableWidth);
        Assert.Equal(width < 890 ? 1 : 0, Grid.GetRow((Border)view.FindName("DistributionPanel")));
        scroll.ScrollToBottom(); view.UpdateLayout();
        var list = (ListBox)view.FindName("SessionsList");
        Assert.Equal(240, list.ActualHeight);
        Assert.True(Bounds(list, view).Bottom <= height + 1);
        Assert.Equal(empty ? Visibility.Visible : Visibility.Collapsed,
            ((TextBlock)view.FindName("DashboardViewEmptyState")).Visibility);
        if (!empty)
        {
            Assert.Equal(Math.Min(5, sessionCount), list.Items.Count);
            list.ScrollIntoView(list.Items[^1]); view.UpdateLayout();
            var last = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1));
            Assert.True(Bounds(last, list).Bottom <= list.ActualHeight + 1, $"Last row: {Bounds(last, list)}, viewport: {list.ActualHeight}");
        }
        if (width >= 890)
        {
            Assert.Equal(((Border)view.FindName("TrendPanel")).ActualHeight,
                ((Border)view.FindName("DistributionPanel")).ActualHeight);
            Assert.Equal(((Border)view.FindName("ActivityPanel")).ActualHeight,
                ((Border)view.FindName("GamesPanel")).ActualHeight);
        }
        var cards = Descendants<Border>((ItemsControl)view.FindName("MetricCards"))
            .Where(border => border.DataContext is EarningsTracker.ViewModels.DashboardMetric).ToList();
        Assert.Equal(5, cards.Count);
        var contentHeight = cards.Max(card => card.Child.DesiredSize.Height + card.Padding.Top + card.Padding.Bottom
            + card.BorderThickness.Top + card.BorderThickness.Bottom);
        Assert.All(cards, card => Assert.InRange(card.ActualHeight - contentHeight, -1, 1));
        scroll.ScrollToTop(); view.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "DashboardVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"dashboard-{width}-{height}-{sessionCount}.png"));
        encoder.Save(file);
    });

    private static Session Recording(DateTime day, decimal amount, SessionStatus status = SessionStatus.Verified) => new()
    {
        GameName = "Recorded game", Date = day, RecordedDate = day, PostedDate = null,
        Status = status, Earnings = amount, Duration = TimeSpan.FromMinutes(5)
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Rect Bounds(FrameworkElement element, Visual parent) =>
        element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
}

