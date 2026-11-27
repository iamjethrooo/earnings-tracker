using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class StatisticsOverviewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 720, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task OverviewSeparatesCalendarAnalysisAndCycleScopes(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var day = new DateTime(2026, 11, 22);
        using (var db = database.CreateContext())
        {
            var games = new[] { "Predecessor", "MAVRIX", "Wreckreation" }.Select(name =>
                new Game { Name = name, MinutesCap = 3000, RatePerHour = 2, CapResetDate = day.AddDays(-21) }).ToArray();
            db.Games.AddRange(games);
            for (var index = 0; index < 30; index++)
            {
                var recorded = day.AddDays(index - 29);
                var earned = index is 29 or 27 ? 16m : index is 28 or 8 ? 12m : 14m;
                db.Sessions.Add(new Session
                {
                    Game = games[index % games.Length], GameName = games[index % games.Length].Name,
                    RecordedDate = recorded, Date = recorded, PostedDate = recorded,
                    Duration = TimeSpan.FromHours((double)(earned / 2)), Earnings = earned,
                    Status = SessionStatus.Verified
                });
            }
            // A signed adjustment tests the chart's negative scale without altering calendar totals.
            db.Payouts.Add(new Payout { Date = day.AddDays(-25), Type = "Earnings correction", Status = "Verified",
                Amount = -20, ReconciliationState = "Separate" });
            db.Sessions.Add(new Session { GameName = "Unlinked historical recording", RecordedDate = day.AddDays(-50),
                Date = day.AddDays(-50), Status = SessionStatus.Verified, Earnings = 5 });
            db.SaveChanges();
        }
        var view = new StatisticsView(database.Options);
        var window = new MainWindow(page => page == NavigationPage.Statistics ? view : new UserControl())
        {
            Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false
        };
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        void Capture(string name) => Save((FrameworkElement)window.Content, $"{name}-{width}x{height}-{scale:0.0}", scale);
        try
        {
            window.Show();
            window.NavigateTo(NavigationPage.Statistics);
            await Settle();
            await view.LoadStatistics(asOfDate: day);
            var range = (ComboBox)view.FindName("EarningsRangeComboBox");
            Assert.Equal(4, range.SelectedIndex); // Preserve Current payout as the starting range.
            range.SelectedIndex = 0;
            await Settle();
            Assert.Equal(16m, view.TodayEarnings);
            Assert.Equal(100m, view.ThisWeekEarnings);
            Assert.Equal(308m, view.ThisMonthEarnings);
            Assert.Equal("Nov 16 – Nov 22", view.CalendarWeekDescription);
            Assert.Equal("Nov 1 – Nov 22", view.CalendarMonthDescription);
            var calendar = (view.TodayEarnings, view.ThisWeekEarnings, view.ThisMonthEarnings);
            var cycles = view.GameCapRows.ToArray();
            var canvas = (Canvas)view.FindName("EarningsTrendCanvas");
            var labels = canvas.Children.OfType<TextBlock>().Select(label => label.Text).ToList();
            Assert.Single(labels, label => label == "Today*");
            Assert.Contains("Oct 24", labels);
            Assert.DoesNotContain("Nov 22", labels);
            Assert.Contains(view.TrendPeriods, period => period.TotalEarnings < 0);
            Assert.Contains("unlinked", view.GameAssociationNotice);
            var scroll = (ScrollViewer)view.FindName("StatisticsScroll");
            Assert.Equal(0, scroll.ScrollableWidth);
            Capture("overview");

            ((ComboBox)view.FindName("GameEarningsMode")).SelectedIndex = 1;
            Assert.All(view.GameEarnings, game => Assert.NotNull(game.VerifiedHourlyRate));
            range.SelectedIndex = 1;
            await Settle();
            Assert.Equal(calendar, (view.TodayEarnings, view.ThisWeekEarnings, view.ThisMonthEarnings));
            Assert.Equal(cycles, view.GameCapRows.ToArray());
            Assert.DoesNotContain(canvas.Children.OfType<TextBlock>(), label => label.Text == "Today*");
            var exactValues = (Expander)view.FindName("TrendValuesExpander");
            exactValues.IsExpanded = true;
            await Settle();
            Assert.True(VirtualizingPanel.GetIsVirtualizing((ListBox)view.FindName("TrendValuesList")));
            Assert.True(VirtualizingPanel.GetIsVirtualizing((ListView)view.FindName("GameCapList")));
            scroll.ScrollToEnd();
            await Settle();
            Capture("cycles-and-values");
            using var check = database.CreateContext();
            Assert.Empty(check.SessionChanges);
            Assert.Empty(check.PayoutChanges);
        }
        finally { window.Close(); }
    });

    private static void Save(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "StatisticsOverviewVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
