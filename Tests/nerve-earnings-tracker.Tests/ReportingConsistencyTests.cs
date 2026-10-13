using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;
using nerve_earnings_tracker.Views;

namespace nerve_earnings_tracker.Tests
{
    [Collection("Wpf")]
    [Trait("Category", "Regression")]
    public class ReportingConsistencyTests(WpfTestHost host)
    {
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(12)]
        public Task DateOnlyAndEarlyRecordingsUseTheSameDayAsAdjustments(int currentHour) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            var day = new DateTime(2026, 6, 1);
            using (var db = database.CreateContext())
            {
                db.Sessions.AddRange(Recording(day, null, 10m, 60), Recording(day.AddHours(4).AddMinutes(59), null, 2m, 30),
                    Recording(day.AddHours(5), null, 3m, 15));
                // Verification occurs later; earnings still belong to the recorded day.
                foreach (var session in db.Sessions.Local)
                {
                    session.IsForceVerified = true;
                    session.PostedDate = day.AddDays(10);
                }
                db.Payouts.AddRange(Adjustment(day, -2m), Adjustment(day, 100m, "NeedsReview"),
                    new Payout { Date = day, Type = "Payment", Status = "Paid", Amount = -100m });
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            await view.LoadStatistics(asOfDate: day.AddHours(currentHour));

            Assert.Equal(13m, view.TodayEarnings);
            Assert.Equal(13m, view.ThisWeekEarnings);
            Assert.Equal(13m, view.ThisMonthEarnings);
            using var read = database.CreateContext();
            Assert.All(read.Sessions, session => Assert.Equal(day.AddDays(10), session.PostedDate));
            Assert.Equal(13m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).TotalEarned);
        });

        [Theory]
        [InlineData("2026-06-01", 10, 10)]
        [InlineData("2026-06-07", 15, 15)]
        [InlineData("2027-01-01", 15, 10)]
        public Task CalendarWeekAndMonthBoundariesDoNotMoveMidnightRecordings(string dayText, int week, int month) => host.RunAsync(async () =>
        {
            var day = DateTime.ParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Sessions.AddRange(Recording(day.AddDays(-1).AddHours(12), null, 5m), Recording(day, null, 10m));
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            await view.LoadStatistics(asOfDate: day.AddHours(12));

            Assert.Equal(10m, view.TodayEarnings);
            Assert.Equal(week, view.ThisWeekEarnings);
            Assert.Equal(month, view.ThisMonthEarnings);
        });

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public Task ChartBucketsPreserveCalendarDatesAndExcludeFutureEarnings(int rangeIndex) => host.RunAsync(async () =>
        {
            var day = new DateTime(2026, 6, 1);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Sessions.AddRange(Recording(day, null, 10m), Recording(day.AddDays(1), null, 99m));
                db.Payouts.Add(Adjustment(day, -2m));
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            Assert.IsType<ComboBox>(view.FindName("EarningsRangeComboBox")).SelectedIndex = rangeIndex;
            await view.LoadStatistics(asOfDate: day.AddHours(12));
            view.Measure(new Size(1000, 1000));
            view.Arrange(new Rect(0, 0, 1000, 1000));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            var canvas = Assert.IsType<Canvas>(view.FindName("EarningsTrendCanvas"));
            var tooltips = canvas.Children.OfType<Ellipse>().Where(dot => dot.ToolTip is ToolTip)
                .Select(dot => Assert.IsType<TextBlock>(Assert.IsType<ToolTip>(dot.ToolTip).Content).Text).ToList();
            Assert.NotEmpty(tooltips);
            Assert.Single(tooltips, text => text.Contains("Total earnings: $8.00"));
            Assert.DoesNotContain(tooltips, text => text.Contains("$99.00"));
            var expectedDate = rangeIndex switch
            {
                0 => "June 1, 2026", 1 => "Week of June 1, 2026", _ => "June 2026"
            };
            var tooltip = Assert.Single(tooltips, text => text.Contains("Total earnings: $8.00"));
            Assert.Contains(expectedDate, tooltip);
            Assert.Contains("Hours: 1.00", tooltip);
            Assert.Equal(10m, Assert.Single(view.GameEarnings).Earnings);
        });

        [Fact]
        public Task CapUtilizationUsesExplicitLinksAndMatchesGameDetails() => host.RunAsync(async () =>
        {
            var day = new DateTime(2026, 6, 1);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Games.AddRange(new Game { Id = 1, Name = "Example", MinutesCap = 120 },
                    new Game { Id = 2, Name = "Example", MinutesCap = 60 },
                    new Game { Id = 3, Name = "Example", MinutesCap = 120, IsArchived = true });
                db.Sessions.AddRange(Recording(day, 1, 10m, 60), Recording(day, 2, 5m, 30),
                    Recording(day, null, 3m, 20), Recording(day, null, 7m, 15), Recording(day, 3, 20m, 120));
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            await view.LoadStatistics(asOfDate: day.AddHours(12));

            Assert.Equal(2, view.GameCapRows.Count);
            Assert.Equal(90m, view.GameCapRows.Sum(row => row.VerifiedMinutes));
            Assert.Equal(15m, view.GameCapRows.Sum(row => row.Earnings));
            Assert.Equal(2, view.UnlinkedSessionCount);
            Assert.Equal(45m, view.TodayEarnings); // Unlinked and archived earnings remain in the ledger.
            using var read = database.CreateContext();
            foreach (var game in read.Games.Where(game => !game.IsArchived).OrderBy(game => game.MinutesCap))
            {
                var details = new GameDetailsViewModel(game, read.Sessions.Where(session => session.GameId == game.Id).ToList());
                var row = Assert.Single(view.GameCapRows, row => row.MinutesCap == game.MinutesCap);
                Assert.Equal(details.TotalMinutes, row.VerifiedMinutes);
                Assert.Equal(details.TotalEarnings, row.Earnings);
            }
            Assert.Equal(2, read.Sessions.Count(session => session.GameId == null));
            Assert.Empty(read.SessionChanges);
        });

        [Fact]
        public Task UnlinkedRecordingDoesNotCreateACapRowEvenForAUniqueName() => host.RunAsync(async () =>
        {
            var day = new DateTime(2026, 6, 1);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Games.Add(new Game { Id = 1, Name = "Example", MinutesCap = 60 });
                db.Sessions.Add(Recording(day, null, 10m));
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            await view.LoadStatistics(asOfDate: day.AddHours(12));
            Assert.Empty(view.GameCapRows);
            Assert.Equal(1, view.UnlinkedSessionCount);
            await Dispatcher.Yield();
            var notice = Assert.IsType<TextBlock>(view.FindName("GameAssociationNoticeText"));
            Assert.Contains("1 recording has no linked game", notice.Text);
            Assert.Equal(Visibility.Visible, notice.Visibility);

            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(day, 1, 10m)]));
            await view.LoadStatistics(asOfDate: day.AddHours(12));
            Assert.Equal(60m, Assert.Single(view.GameCapRows).VerifiedMinutes);
            Assert.Equal(0, view.UnlinkedSessionCount);
            await Dispatcher.Yield();
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            using var read = database.CreateContext();
            Assert.Equal(1, Assert.Single(read.Sessions).GameId);
            using var audit = JsonDocument.Parse(Assert.Single(read.SessionChanges).AfterValuesJson);
            Assert.Equal(1, audit.RootElement.GetProperty("GameId").GetInt32());
        });

        [Fact]
        public Task CapCycleUsesRecordedDateWithLegacyFallback() => host.RunAsync(async () =>
        {
            var day = new DateTime(2026, 6, 1);
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Games.Add(new Game { Id = 1, Name = "Example", MinutesCap = 120, CapResetDate = day });
                var session = Recording(day, 1, 10m);
                session.Date = day.AddDays(-1); // RecordedDate is the explicit recording value.
                var legacy = Recording(day, 1, 5m, 30);
                legacy.RecordedDate = default;
                db.Sessions.AddRange(session, legacy);
                db.SaveChanges();
            }
            var view = new StatisticsView(database.Options);
            await view.LoadStatistics(asOfDate: day.AddHours(12));
            Assert.Equal(15m, view.TodayEarnings);
            Assert.Equal(90m, Assert.Single(view.GameCapRows).VerifiedMinutes);
            using var read = database.CreateContext();
            var details = new GameDetailsViewModel(read.Games.Single(), read.Sessions.ToList());
            Assert.Equal(90m, details.TotalMinutes);
            Assert.Equal(15m, details.TotalEarnings);
        });

        private static Session Recording(DateTime date, int? gameId, decimal earnings, int minutes = 60) => new()
        {
            GameId = gameId, GameName = "Example", Date = date, RecordedDate = date, PostedDate = date,
            Status = SessionStatus.Verified, Earnings = earnings, Duration = TimeSpan.FromMinutes(minutes)
        };

        private static Payout Adjustment(DateTime date, decimal amount, string state = "Separate") => new()
        {
            Date = date, Type = "Earnings correction", Status = "Verified", Amount = amount, ReconciliationState = state
        };
    }
}
