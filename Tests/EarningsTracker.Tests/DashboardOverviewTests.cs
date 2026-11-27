using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class DashboardOverviewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 720, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task OverviewKeepsAccountScopeWarningsAndActionsAcrossPeriodChanges(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var day = new DateTime(2026, 11, 22);
        using (var db = database.CreateContext())
        {
            var games = new[]
            {
                new Game { Name = "Predecessor", RatePerHour = 2, MinutesCap = 3000 },
                new Game { Name = "MAVRIX", RatePerHour = 2, MinutesCap = 3000 },
                new Game { Name = "Wreckreation", RatePerHour = 2, MinutesCap = 3000 }
            };
            db.Games.AddRange(games);
            var payment = new Payout { Date = day.AddDays(-17), Type = "Payment", Status = "Paid", Amount = -120 };
            db.Payouts.AddRange(payment,
                new Payout { Date = day.AddDays(-38), Type = "Payment", Status = "Paid", Amount = -786 },
                new Payout { Date = day.AddDays(-30), Type = "Payment", Status = "Processing", Amount = -50 });
            db.SaveChanges();
            for (var index = 0; index < 30; index++)
            {
                var earned = index == 0 ? 12m : index == 29 ? 16m : 14m;
                var recorded = day.AddDays(index - 29).AddHours(14);
                var game = games[index % games.Length];
                db.Sessions.Add(new Session
                {
                    Game = game, GameName = game.Name, Date = recorded, RecordedDate = recorded,
                    PostedDate = recorded, Status = SessionStatus.Verified, Earnings = earned,
                    Duration = TimeSpan.FromHours((double)(earned / game.RatePerHour)),
                    HasExplicitPayoutAssignment = true, AssignedPayoutId = index < 24 ? payment.Id : null
                });
            }
            db.Sessions.AddRange(
                new Session { Game = games[2], GameName = games[2].Name, Date = day.AddHours(18), RecordedDate = day.AddHours(18),
                    Status = SessionStatus.Pending, Earnings = 24, Duration = TimeSpan.FromHours(12) },
                new Session { Game = games[0], GameName = games[0].Name, Date = day.AddDays(-40), RecordedDate = day.AddDays(-40),
                    Status = SessionStatus.Verified, Earnings = 8, Duration = TimeSpan.FromHours(4) });
            db.SaveChanges();
            CatalogService.ImportBundled(db);
        }

        var view = new DashboardView(database.Options);
        var window = new MainWindow(_ => view)
        {
            Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false
        };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await view.LoadDashboardData(asOfDate: day);
            var selector = (ComboBox)view.FindName("PeriodSelector");
            Assert.Equal(3, selector.SelectedIndex); // Keep Current payout as the starting scope.
            Assert.Equal(86m, view.Balance);
            Assert.Equal("$906.00", view.PaidDisplay);
            Assert.Equal("$50.00", view.ProcessingDisplay);
            foreach (var name in new[] { "PaidValue", "ProcessingValue" })
                Assert.Same(view.FindResource("DefaultTextColor"), ((TextBlock)view.FindName(name)).Foreground);
            foreach (var period in new[] { 0, 2, 1 })
            {
                selector.SelectedIndex = period;
                Assert.Equal(86m, view.Balance);
                Assert.Equal("$906.00", view.PaidDisplay);
                Assert.Equal("$50.00", view.ProcessingDisplay);
            }
            Assert.Equal("$420.00", view.Metrics[0].Value);
            Assert.Equal("$24.00", view.Metrics[1].Value);
            Assert.Equal("Awaiting verification", view.Metrics[1].Detail);
            Assert.Equal("$120.00", view.Metrics[2].Value);
            Assert.Equal($"{222:N1} h", view.Metrics[3].Value);
            Assert.Contains("1 session without posted dates", view.BalanceExclusionNotice);

            var imports = 0;
            var sessions = 0;
            view.ImportSessionsRequested += (_, _) => imports++;
            view.ViewAllSessionsRequested += (_, _) => sessions++;
            var import = (Button)view.FindName("ImportSessionsButton");
            Assert.True(import.Focus());
            import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)view.FindName("ViewSessionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, imports);
            Assert.Equal(1, sessions);

            foreach (var row in view.RecentSessions) _ = row.Cover;
            for (var attempt = 0; attempt < 40 && view.RecentSessions.Any(row => row.Cover == null); attempt++)
                await Task.Delay(50);
            Assert.All(view.RecentSessions, row => Assert.NotNull(row.Cover));
            window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var scroll = (ScrollViewer)view.FindName("TotalsScroll");
            Assert.Equal(0, scroll.ScrollableWidth);
            Assert.False(((Expander)view.FindName("MoreInsights")).IsExpanded);
            scroll.ScrollToTop();
            window.UpdateLayout();
            var list = (ListBox)view.FindName("SessionsList");
            if (width == 1488)
            {
                for (var index = 0; index < 3; index++)
                {
                    var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                    var bounds = row.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(row.RenderSize));
                    Assert.True(bounds.Bottom <= height - 16, $"Recent row {index + 1} is clipped: {bounds}");
                }
            }
            Capture((FrameworkElement)window.Content, $"overview-{width}x{height}-{scale:0.0}", scale);

            list.BringIntoView();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            list.ScrollIntoView(list.Items[^1]);
            window.UpdateLayout();
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            foreach (var item in view.RecentSessions)
            {
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(item);
                if (row == null) continue;
                var title = Descendants<TextBlock>(row).Single(text => text.Text == item.Session.GameName);
                Assert.True(title.ActualWidth > 0);
                Assert.True(title.TransformToAncestor(row).TransformBounds(new Rect(title.RenderSize)).Right <= row.ActualWidth);
                foreach (var badge in Descendants<Border>(row).Where(border => border.Style == view.FindResource("DashboardSessionStatusBadge")))
                    Assert.True(badge.TransformToAncestor(row).TransformBounds(new Rect(badge.RenderSize)).Right <= row.ActualWidth + 1);
            }
            Capture((FrameworkElement)window.Content, $"recent-{width}x{height}-{scale:0.0}", scale);
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "DashboardOverviewVisuals");
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
