using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Views;

namespace nerve_earnings_tracker.Tests;

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
    [InlineData(740, 420, false)]
    [InlineData(1000, 688, false)]
    [InlineData(1480, 688, false)]
    [InlineData(740, 568, true)]
    public Task CardsAndRecentActivityStayWithinTheirViewports(double width, double height, bool empty) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        if (!empty)
        {
            using var db = database.CreateContext();
            var game = new Game { Name = "A long game title with an expansion and complete edition" };
            db.Games.Add(game);
            for (var i = 0; i < 7; i++)
            {
                var session = Recording(DateTime.Today.AddHours(i), 123456.78m);
                session.Game = game; session.GameName = game.Name;
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
        var cards = (UniformGrid)view.FindName("MetricCards");
        Assert.Equal(2, cards.Columns);
        Assert.Equal(2, cards.Children.Count);
        var scroll = (ScrollViewer)view.FindName("TotalsScroll");
        Assert.Equal(0, scroll.ScrollableWidth);
        scroll.ScrollToBottom(); view.UpdateLayout();
        var balance = (Border)cards.Children[1];
        Assert.True(Bounds(balance, scroll).Bottom <= scroll.ActualHeight + 1);
        Assert.All(cards.Children.OfType<Border>(), card =>
        {
            Assert.True(card.ActualWidth > 250);
            Assert.All(((StackPanel)card.Child).Children.OfType<TextBlock>(), text =>
                Assert.True(text.ActualWidth <= card.ActualWidth - card.Padding.Left - card.Padding.Right + 1));
        });
        var list = (ListBox)view.FindName("SessionsList");
        Assert.True(list.ActualHeight >= 70, $"List height: {list.ActualHeight}");
        Assert.True(Bounds(list, view).Bottom <= height + 1);
        Assert.Equal(empty ? Visibility.Visible : Visibility.Collapsed,
            ((TextBlock)view.FindName("DashboardViewEmptyState")).Visibility);
        if (!empty)
        {
            Assert.Equal(5, list.Items.Count);
            list.ScrollIntoView(list.Items[4]); view.UpdateLayout();
            var last = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(4));
            Assert.True(Bounds(last, list).Bottom <= list.ActualHeight + 1, $"Last row: {Bounds(last, list)}, viewport: {list.ActualHeight}");
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "DashboardVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"dashboard-{width}-{height}-{empty}.png"));
        encoder.Save(file);
    });

    private static Session Recording(DateTime day, decimal amount, SessionStatus status = SessionStatus.Verified) => new()
    {
        GameName = "Recorded game", Date = day, RecordedDate = day, PostedDate = null,
        Status = status, Earnings = amount, Duration = TimeSpan.FromMinutes(5)
    };

    private static Rect Bounds(FrameworkElement element, Visual parent) =>
        element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
}

