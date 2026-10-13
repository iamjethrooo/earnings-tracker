using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using nerve_earnings_tracker.Data;
using System.Data.Common;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Exceptions;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace nerve_earnings_tracker.Tests
{
    [CollectionDefinition("Wpf", DisableParallelization = true)]
    public class WpfCollection : ICollectionFixture<WpfTestHost> { }

    [Collection("Wpf")]
    [Trait("Category", "Regression")]
    public class DatabaseErrorHandlingTests(WpfTestHost host)
    {
        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Statistics")]
        [InlineData("Payouts")]
        public Task FailedInitialLoadShowsErrorInsteadOfValidZeroTotals(string page) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
                db.Database.ExecuteSqlRaw("DROP TABLE Sessions");
            var view = CreatePage(page, database);

            await Reload(view);

            var status = Assert.IsType<TextBlock>(view.FindName("LoadStatusText"));
            Assert.Contains("unavailable", status.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Visibility.Visible, Assert.IsType<Border>(view.FindName("LoadStatusPanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsAssignableFrom<FrameworkElement>(view.FindName("TotalsPanel")).Visibility);
        });

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Statistics")]
        [InlineData("Payouts")]
        public Task LoadedRetryAndSavedNotificationHandleDatabaseErrors(string page) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            Seed(database);
            using (var db = database.CreateContext())
                db.Database.ExecuteSqlRaw("ALTER TABLE Payouts RENAME TO UnavailablePayouts");
            var view = CreatePage(page, database);
            try
            {
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                if (view is PayoutsView initialPayments) await initialPayments.RefreshCompletion;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var status = Assert.IsType<TextBlock>(view.FindName("LoadStatusText"));
                Assert.Contains("unavailable", status.Text, StringComparison.OrdinalIgnoreCase);
                using (var db = database.CreateContext())
                    db.Database.ExecuteSqlRaw("ALTER TABLE UnavailablePayouts RENAME TO Payouts");

                Assert.IsType<Button>(view.FindName("RetryLoadButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (view is PayoutsView retriedPayments) await retriedPayments.RefreshCompletion;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(view.FindName("LoadStatusPanel")).Visibility);
                var before = ReadSnapshot(view);
                using (var db = database.CreateContext())
                    db.Database.ExecuteSqlRaw("ALTER TABLE Payouts RENAME TO UnavailablePayouts");

                GameService.SaveCapResetDate(1, DateTime.Today, database.Options);
                if (view is PayoutsView savedPayments) await savedPayments.RefreshCompletion;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.Contains("Changes were saved", status.Text);
                Assert.Equal(before, ReadSnapshot(view));
                using var read = database.CreateContext();
                Assert.Equal(DateTime.Today, read.Games.Single().CapResetDate);
            }
            finally { view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
        });

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Statistics")]
        [InlineData("Payouts")]
        public Task FailedReloadRetainsSnapshotAndRetryLoadsCurrentData(string page) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            Seed(database);
            var view = CreatePage(page, database);
            await Reload(view);
            var before = ReadSnapshot(view);
            using (var db = database.CreateContext())
                db.Database.ExecuteSqlRaw("ALTER TABLE Payouts RENAME TO UnavailablePayouts");

            await Reload(view);

            Assert.Equal(before, ReadSnapshot(view));
            Assert.Contains("previous", Assert.IsType<TextBlock>(view.FindName("LoadStatusText")).Text, StringComparison.OrdinalIgnoreCase);
            using (var db = database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("ALTER TABLE UnavailablePayouts RENAME TO Payouts");
                db.Sessions.Single().Earnings = 9m;
                db.SaveChanges();
            }
            await Reload(view);
            Assert.NotEqual(before, ReadSnapshot(view));
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(view.FindName("LoadStatusPanel")).Visibility);
        });

        [Fact]
        public Task DashboardDoesNotPublishTotalsWhenFinalQueryFails() => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            Seed(database);
            var view = new DashboardView(database.Options);
            await view.LoadDashboardData();
            var before = ReadSnapshot(view);
            using (var db = database.CreateContext())
            {
                db.Sessions.Single().Earnings = 9m;
                db.SaveChanges();
                db.Database.ExecuteSqlRaw("ALTER TABLE Games RENAME TO UnavailableGames");
            }

            await view.LoadDashboardData();

            Assert.Equal(before, ReadSnapshot(view));
        });

        [Theory]
        [InlineData("Dashboard")]
        [InlineData("Statistics")]
        public Task RefreshRequestedDuringLoadUsesLatestCommittedData(string page) => host.RunAsync(async () =>
        {
            using var database = new TestDatabase();
            Seed(database);
            var pause = new PausePayoutRead();
            var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(pause).Options;
            UserControl view = page == "Dashboard" ? new DashboardView(options) : new StatisticsView(options);
            var initialLoad = Reload(view);
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                using var db = database.CreateContext();
                db.Sessions.Single().Earnings = 9m;
                db.SaveChanges();
                if (view is DashboardView dashboard)
                    await dashboard.LoadDashboardData(changesSaved: true);
                else
                    await ((StatisticsView)view).LoadStatistics(changesSaved: true);
            }
            finally { pause.Resume.SetResult(); }
            await initialLoad;

            var earnings = view is DashboardView result ? result.TotalEarned : ((StatisticsView)view).GameCapRows.Single().Earnings;
            Assert.Equal(9m, earnings);
        });

        private sealed class PausePayoutRead : DbCommandInterceptor
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                if (command.CommandText.Contains("FROM \"Payouts\"") && !Entered.Task.IsCompleted)
                {
                    Entered.SetResult();
                    await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                return result;
            }
        }

        [Fact]
        public void CapResetNotifiesOnlyAfterSuccessfulCommit()
        {
            using var database = new TestDatabase();
            Seed(database);
            var observed = new List<DateTime?>();
            EventHandler handler = (_, _) =>
            {
                using var read = database.CreateContext();
                observed.Add(read.Games.Single().CapResetDate);
            };
            PayoutService.DataChanged += handler;
            try
            {
                var date = DateTime.Today.AddDays(-1);
                GameService.SaveCapResetDate(1, date.AddHours(12), database.Options);
                Assert.Equal(new DateTime?[] { date }, observed);
                GameService.SaveCapResetDate(1, null, database.Options);
                Assert.Equal(new DateTime?[] { date, null }, observed);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void FailedCapResetPreservesStoredDateAndDoesNotNotify()
        {
            using var database = new TestDatabase();
            Seed(database);
            using (var db = database.CreateContext())
                db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectReset BEFORE UPDATE ON Games BEGIN SELECT RAISE(ABORT, 'test save failure'); END;");
            var notified = false;
            EventHandler handler = (_, _) => notified = true;
            PayoutService.DataChanged += handler;
            try
            {
                Assert.Throws<DbUpdateException>(() => GameService.SaveCapResetDate(1, DateTime.Today, database.Options));
                using var read = database.CreateContext();
                Assert.Null(read.Games.Single().CapResetDate);
                Assert.False(notified);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void CapResetRefreshFailureReportsCommittedChange()
        {
            using var database = new TestDatabase();
            Seed(database);
            EventHandler handler = (_, _) => throw new InvalidOperationException("test refresh failure");
            PayoutService.DataChanged += handler;
            try
            {
                Assert.Throws<PostCommitRefreshException>(() => GameService.SaveCapResetDate(1, DateTime.Today, database.Options));
                using var read = database.CreateContext();
                Assert.Equal(DateTime.Today, read.Games.Single().CapResetDate);
            }
            finally { PayoutService.DataChanged -= handler; }
        }

        [Fact]
        public void InvalidCapResetNeverChangesDatabase()
        {
            using var database = new TestDatabase();
            Seed(database);
            Assert.Throws<ArgumentException>(() => GameService.SaveCapResetDate(1, DateTime.Today.AddDays(1), database.Options));
            Assert.Throws<InvalidOperationException>(() => GameService.SaveCapResetDate(999, null, database.Options));
            using var read = database.CreateContext();
            Assert.Null(read.Games.Single().CapResetDate);
        }

        private static UserControl CreatePage(string page, TestDatabase database) => page switch
        {
            "Dashboard" => new DashboardView(database.Options),
            "Statistics" => new StatisticsView(database.Options),
            _ => new PayoutsView(database.Options)
        };

        private static Task Reload(UserControl view) => view switch
        {
            DashboardView dashboard => dashboard.LoadDashboardData(),
            StatisticsView statistics => statistics.LoadStatistics(),
            PayoutsView payouts => ReloadPayouts(payouts),
            _ => throw new InvalidOperationException()
        };

        private static Task ReloadPayouts(PayoutsView view) => view.RefreshHistory();

        private static string ReadSnapshot(UserControl view) => view switch
        {
            DashboardView dashboard => $"{dashboard.TotalEarned}|{dashboard.TotalPaidOut}|{dashboard.Balance}|{dashboard.TotalSessions}|{string.Join(",", dashboard.RecentSessions.Select(row => row.Session.Earnings))}",
            StatisticsView statistics => $"{statistics.TodayEarnings}|{statistics.ThisWeekEarnings}|{statistics.ThisMonthEarnings}|{string.Join(",", statistics.GameCapRows.Select(row => row.Earnings))}",
            PayoutsView payouts => $"{Assert.IsType<TextBlock>(payouts.FindName("TotalEarnedValue")).Text}|{string.Join(",", payouts.History.Select(row => row.Amount))}",
            _ => throw new InvalidOperationException()
        };

        private static void Seed(TestDatabase database)
        {
            using var db = database.CreateContext();
            var game = new Game { Id = 1, Name = "Hades", MinutesCap = 120 };
            db.Games.Add(game);
            db.Sessions.Add(new Session
            {
                Game = game, GameName = game.Name, Date = DateTime.Today.AddHours(12),
                RecordedDate = DateTime.Today.AddHours(12), PostedDate = DateTime.Today,
                Duration = TimeSpan.FromHours(1), Status = SessionStatus.Verified, Earnings = 3m
            });
            db.SaveChanges();
        }
    }
}
