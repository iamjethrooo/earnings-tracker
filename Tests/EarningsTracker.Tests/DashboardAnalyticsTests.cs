using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using EarningsTracker.Controls;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class DashboardAnalyticsTests(WpfTestHost host)
{
    private static readonly DateTime Day = new(2027, 1, 1);

    [Fact]
    public Task PeriodsReconcileWithStatisticsSharedAccountingAndSessionsWithoutChangingHistory() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var dashboard = View(database);
        await dashboard.LoadDashboardData(asOfDate: Day.AddHours(4));
        var statistics = new StatisticsView(database.Options);
        await statistics.LoadStatistics(asOfDate: Day);
        var sessions = new SessionsViewModel(database.Options);
        await sessions.LoadSessionsAsync();
        using var read = database.CreateContext();
        var rows = read.Sessions.AsNoTracking().ToList();
        var payouts = read.Payouts.AsNoTracking().ToList();
        var totals = PayoutAccounting.Calculate(rows, payouts);
        Assert.Equal(totals.TotalEarned, dashboard.TotalEarned);
        Assert.Equal(totals.TotalPaidOut, dashboard.TotalPaidOut);
        Assert.Equal(totals.Balance, dashboard.Balance);
        Assert.Contains("without posted dates", dashboard.BalanceDescription);
        Assert.Equal(statistics.TodayEarnings, dashboard.TodayEarnings);
        Assert.Equal(statistics.ThisWeekEarnings, dashboard.ThisWeekEarnings);
        Assert.Equal(statistics.ThisMonthEarnings, dashboard.ThisMonthEarnings);
        foreach (var index in new[] { 0, 1, 2 })
        {
            ((ComboBox)dashboard.FindName("PeriodSelector")).SelectedIndex = index;
            var length = new[] { 7, 30, 90 }[index];
            Assert.Equal(length, dashboard.Trend.Count);
            var start = Day.AddDays(1 - length);
            var selected = rows.Where(row => Recorded(row) >= start && Recorded(row) <= Day).ToList();
            var changes = payouts.Where(PayoutAccounting.IsSeparateEarnedAdjustment).Where(row => row.Date.Date >= start && row.Date.Date <= Day).ToList();
            Assert.Equal(selected.Where(row => row.Status == SessionStatus.Verified).Sum(row => row.Earnings) + changes.Sum(row => row.Amount), dashboard.PeriodEarned);
            Assert.Equal(selected.Count, dashboard.Statuses.Sum(status => status.Count));
            Assert.Equal(selected.Where(row => row.Status == SessionStatus.Pending).Sum(row => row.Earnings), dashboard.PeriodPending);
            Assert.Equal(selected.Where(row => row.Status == SessionStatus.Verified).Sum(row => row.Earnings), dashboard.TopGames.Sum(game => game.Earnings));
            Assert.Equal(4, dashboard.Statuses.Count);
            Assert.Equal(totals.Balance, dashboard.Balance);
            Assert.Equal(selected.OrderByDescending(row => row.RecordedDate == default ? row.Date : row.RecordedDate).ThenByDescending(row => row.Id).Take(5).Select(row => row.Id), dashboard.RecentSessions.Select(row => row.Session.Id));
            Assert.DoesNotContain(dashboard.RecentSessions, row => Recorded(row.Session) > Day);
            Assert.Contains(sessions.Sessions, row => row.Session.Id == selected[0].Id);
        }
        ((ComboBox)dashboard.FindName("PeriodSelector")).SelectedIndex = 1;
        Assert.Equal(statistics.TrendPeriods.Sum(period => period.TotalEarnings), dashboard.PeriodEarned);
        var today = dashboard.Trend.Single(row => row.Date == Day);
        Assert.Equal(-3.4567m, today.Earned);
        Assert.Equal(1.2345m, today.Verified);
        Assert.Equal(-4.6912m, today.Adjustments);
        Assert.Equal(2.3456m, today.Pending);
        Assert.Contains(dashboard.TopGames, game => game.Game?.Id == 1);
        Assert.Contains(dashboard.TopGames, game => game.Game?.Id == 2 && game.Game.IsArchived);
        Assert.Contains(dashboard.TopGames, game => game.Game == null && !game.CanNavigate);
        Assert.Equal(90m, totals.TotalPaidOut); // Includes the existing future Processing payment rule.
        Assert.Empty(read.SessionChanges); Assert.Empty(read.GameChanges);
        Assert.Equal(1.2345m, read.Sessions.Single(row => row.RecordedDate == Day).Earnings);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmptyAndRealZeroHaveDifferentChartStatesAndFailuresRetainTheirSnapshot(bool zero) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        if (zero)
        {
            using var db = database.CreateContext();
            db.Sessions.Add(Recording(Day, 0));
            db.Payouts.AddRange(Adjustment(Day, 5), Adjustment(Day, -5));
            db.SaveChanges();
        }
        var view = View(database);
        await view.LoadDashboardData(asOfDate: Day);
        Assert.Equal(0, view.PeriodEarned);
        Assert.Equal(zero ? Visibility.Visible : Visibility.Collapsed, ((DashboardEarningsChart)view.FindName("TrendChart")).Visibility);
        var original = view.Trend;
        await Layout(view, 740, 560);
        Capture(view, zero ? "real-zero" : "empty-period");
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO UnavailableMetadata");
        await view.LoadDashboardData(changesSaved: true, asOfDate: Day);
        Assert.Same(original, view.Trend);
        Assert.Contains("Changes were saved", ((TextBlock)view.FindName("LoadStatusText")).Text);
        Assert.Contains("out of date", ((TextBlock)view.FindName("LoadStatusText")).Text);
        await Layout(view, 740, 560); Capture(view, zero ? "stale-zero" : "stale-empty");
        var failed = View(database);
        await failed.LoadDashboardData(asOfDate: Day);
        Assert.Empty(failed.Metrics);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)failed.FindName("TotalsPanel")).Visibility);
        Assert.Contains("unavailable", ((TextBlock)failed.FindName("LoadStatusText")).Text);
        await Layout(failed, 740, 560); Capture(failed, "initial-error");
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE UnavailableMetadata RENAME TO GameMetadata");
        await view.LoadDashboardData(asOfDate: Day);
        Assert.Equal(Visibility.Collapsed, ((Border)view.FindName("LoadStatusPanel")).Visibility);
    });

    [Fact]
    public Task PeriodAndActivityFocusRemainAvailableInAnOffscreenWindow() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase(); Seed(database);
        var view = View(database);
        var window = new Window { Content = view, Width = 1000, Height = 720, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await view.LoadDashboardData(asOfDate: Day);
            var selector = (ComboBox)view.FindName("PeriodSelector");
            Assert.True(selector.Focus()); Assert.True(selector.IsKeyboardFocusWithin);
            selector.SelectedIndex = 0; Assert.Equal(7, view.Trend.Count);
            selector.SelectedIndex = 2; Assert.Equal(90, view.Trend.Count);
            var all = Descendants<Button>(view).Single(button => Equals(button.Content, "Sessions →"));
            all.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(all.Focus());
            Assert.True(all.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            var scroll = (ScrollViewer)view.FindName("TotalsScroll"); scroll.ScrollToBottom(); view.UpdateLayout();
            Assert.True(Bounds(selector, view).Bottom < 120); // Header stays outside page scrolling.
            var list = (ListBox)view.FindName("SessionsList");
            var item = view.RecentSessions.First(row => row.Game != null);
            list.ScrollIntoView(item); view.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
            int? target = null; view.ViewGameRequested += (_, game) => target = game.Id;
            Assert.True(row.Focus()); row.IsSelected = true;
            Assert.Equal(item.Game!.Id, target);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(740, 560)]
    [InlineData(1212, 900)]
    [InlineData(1572, 940)]
    public Task ApprovedLayoutChartFallbackAndNavigationRenderWithinBoundedViewports(double width, double height) => host.RunAsync(async () =>
    {
        using var bindings = new BindingTrace();
        using var database = new TestDatabase(); Seed(database);
        var view = View(database);
        await view.LoadDashboardData(asOfDate: Day);
        await Layout(view, width, height);
        Assert.Equal(5, view.Metrics.Count);
        Assert.Equal(width < 890 ? 1 : 0, Grid.GetRow((Border)view.FindName("DistributionPanel")));
        var scroll = (ScrollViewer)view.FindName("TotalsScroll");
        Assert.Equal(0, scroll.ScrollableWidth);
        var selector = (ComboBox)view.FindName("PeriodSelector");
        Assert.True(selector.IsEnabled);
        Assert.True(Bounds(selector, view).Bottom <= height);
        var chart = (DashboardEarningsChart)view.FindName("TrendChart");
        Assert.Equal(2, chart.Children.OfType<Polyline>().Count());
        Assert.All(chart.Children.OfType<Polyline>().SelectMany(line => line.Points), point => Assert.InRange(point.Y, 0, chart.ActualHeight));
        Assert.Contains(chart.Children.OfType<Rectangle>(), target => ((ToolTip)target.ToolTip).Content.ToString()!.Contains("Adjustments"));
        Capture(view, $"dashboard-{width}");
        ((Expander)view.FindName("DailyValues")).IsExpanded = true;
        await Layout(view, width, height);
        var values = Descendants<ListBox>((Expander)view.FindName("DailyValues")).Single();
        Assert.Equal(30, values.Items.Count); Assert.Equal(140, values.Height);
        values.ScrollIntoView(values.Items[^1]); values.UpdateLayout();
        Assert.True(Descendants<ListBoxItem>(values).Count() < 30);
        Assert.True(VirtualizingPanel.GetIsVirtualizing(values));
        Capture(view, $"dashboard-values-{width}");
        scroll.ScrollToBottom(); await Layout(view, width, height);
        var recent = (ListBox)view.FindName("SessionsList");
        Assert.Equal(240, recent.Height); Assert.True(recent.Items.Count <= 5);
        recent.ScrollIntoView(recent.Items[^1]); recent.UpdateLayout();
        var last = (ListBoxItem)recent.ItemContainerGenerator.ContainerFromIndex(recent.Items.Count - 1);
        Assert.True(Bounds(last, recent).Bottom <= recent.ActualHeight + 1);
        int? gameId = null; view.ViewGameRequested += (_, game) => gameId = game.Id;
        var linked = view.RecentSessions.First(row => row.Game != null);
        recent.SelectedItem = linked;
        Assert.Equal(linked.Game!.Id, gameId);
        var gameButton = Descendants<Button>((Border)view.FindName("GamesPanel")).First(button => button.DataContext is DashboardGame { Game: not null });
        gameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(((DashboardGame)gameButton.DataContext).Game!.Id, gameId);
        var all = false; view.ViewAllSessionsRequested += (_, _) => all = true;
        Descendants<Button>(view).Single(button => Equals(button.Content, "Sessions →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(all);
        Capture(view, $"dashboard-bottom-{width}");
        if (width == 1212) Capture(view, "dashboard-150pct", 1.5);
        using var read = database.CreateContext(); Assert.Empty(read.SessionChanges);
    });

    private static void Seed(TestDatabase database)
    {
        using var db = database.CreateContext();
        db.Games.AddRange(new Game { Id = 1, Name = "Same game · a long edition title" }, new Game { Id = 2, Name = "Same game · a long edition title", IsArchived = true });
        db.Sessions.AddRange(Recording(Day, 1.2345m, gameId: 1), Recording(Day.AddHours(1), 2.3456m, SessionStatus.Pending, 1),
            Recording(Day.AddDays(-6), 4, gameId: 2), Recording(Day.AddDays(-29), 3), Recording(Day.AddDays(-89), 2),
            Recording(Day.AddDays(-90), 999), Recording(Day.AddDays(1), 777, gameId: 1),
            Recording(Day.AddHours(2), 100, SessionStatus.Rejected), Recording(Day.AddHours(3), 200, SessionStatus.NotUploaded));
        var legacy = Recording(Day.AddDays(-1), 0, gameId: 1); legacy.RecordedDate = default; db.Sessions.Add(legacy);
        db.Payouts.AddRange(Adjustment(Day, -4.6912m), Adjustment(Day.AddDays(-29), 2), Adjustment(Day.AddDays(1), 555),
            new Payout { Date = Day, Type = "Bonus", Status = "Verified", Amount = 100, ReconciliationState = "NeedsReview" },
            new Payout { Date = Day, Type = "Bonus", Status = "Verified", Amount = 200, ReconciliationState = "Separate", IsAdjustmentIncludedInSessionEarnings = true },
            new Payout { Date = Day.AddDays(-1), Type = "Payment", Status = "Paid", Amount = -30 },
            new Payout { Date = Day.AddDays(1), Type = "Payment", Status = "Processing", Amount = -60 });
        db.SaveChanges();
    }
    private static Session Recording(DateTime date, decimal amount, SessionStatus status = SessionStatus.Verified, int? gameId = null) => new()
    { Date = date, RecordedDate = date, GameId = gameId, GameName = gameId.HasValue ? "Same game · a long edition title" : "Historical unlinked", Status = status, Earnings = amount, Duration = TimeSpan.FromMinutes(30), PostedDate = gameId == 1 ? null : date.AddDays(3) };
    private static Payout Adjustment(DateTime date, decimal amount) => new() { Date = date, Type = "Earnings correction", Status = "Verified", Amount = amount, ReconciliationState = "Separate" };
    private static DateTime Recorded(Session row) => (row.RecordedDate == default ? row.Date : row.RecordedDate).Date;
    private static DashboardView View(TestDatabase database) => new(database.Options) { FontFamily = (FontFamily)Application.Current.FindResource("AppFont"), Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor"), UseLayoutRounding = true };
    private static async Task Layout(FrameworkElement view, double width, double height)
    { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
    private static Rect Bounds(FrameworkElement child, Visual ancestor) => child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void Capture(FrameworkElement view, string name, double scale = 1)
    {
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "DashboardAnalyticsVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(view.ActualWidth * scale), (int)(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
    private sealed class BindingTrace : IDisposable
    {
        private readonly StringWriter _output = new();
        private readonly TextWriterTraceListener _listener;
        private readonly SourceLevels _previous = PresentationTraceSources.DataBindingSource.Switch.Level;
        public BindingTrace() { _listener = new(_output); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning; PresentationTraceSources.DataBindingSource.Listeners.Add(_listener); }
        public void Dispose() { PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener); PresentationTraceSources.DataBindingSource.Switch.Level = _previous; _listener.Flush(); var output = _output.ToString(); _listener.Dispose(); _output.Dispose(); Assert.True(string.IsNullOrWhiteSpace(output), output); }
    }
}
