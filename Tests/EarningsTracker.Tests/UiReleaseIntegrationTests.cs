using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class UiReleaseIntegrationTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1000, 600)]
    [InlineData(1440, 960)]
    [InlineData(1800, 1000)]
    public Task ShellPageRecreationPreservesAuditedEditsReportsAndPersonalGameState(double width, double height) => host.RunAsync(async () =>
    {
        using var trace = new BindingTrace();
        using var database = new TestDatabase();
        var today = DateTime.Today;
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Id = 1, Name = "Synthetic integrated game", MinutesCap = 120, RatePerHour = 1.5m });
            db.Sessions.Add(new Session { GameId = 1, GameName = "Synthetic integrated game", Date = today,
                RecordedDate = today, PostedDate = null, Status = SessionStatus.Verified, Earnings = 1.2345m, Duration = TimeSpan.FromMinutes(12.3456) });
            db.Payouts.Add(new Payout { Date = today, Type = "Earnings correction", Status = "Verified", Amount = -1, ReconciliationState = "Separate" });
            db.SubscriptionAvailability.Add(new SubscriptionAvailability { GameId = 1, Service = "PC Game Pass", Region = "PH", Platform = "PC", Included = true, VerifiedAt = DateTime.UtcNow.AddDays(-8), Provenance = "Synthetic saved evidence" });
            db.GameOwnership.Add(new GameOwnership { GameId = 1, IsOwned = false, PurchaseAmount = 123.4567m, Currency = "PHP", Source = "Synthetic source" });
            db.SaveChanges();
        }
        MainWindow? window = null;
        // Inject explicit fixture storage. Event wiring mirrors production CreatePage; selecting Games
        // before OpenGameDetails keeps the coordinator's existing-page path on explicit test options.
        void OpenGame(Game game) { window!.NavigateTo(NavigationPage.Games); window.OpenGameDetails(game); }
        UserControl Create(NavigationPage page)
        {
            switch (page)
            {
                case NavigationPage.Dashboard:
                    var dashboard = new DashboardView(database.Options);
                    dashboard.ViewAllSessionsRequested += (_, _) => window!.NavigateTo(NavigationPage.Sessions);
                    dashboard.ViewGameRequested += (_, game) => OpenGame(game);
                    return dashboard;
                case NavigationPage.Sessions:
                    var sessions = new SessionsView(database.Options);
                    sessions.ViewGameRequested += (_, game) => OpenGame(game);
                    return sessions;
                case NavigationPage.Games: return new GamesView(database.Options);
                case NavigationPage.Statistics: return new StatisticsView(database.Options);
                case NavigationPage.Payments: return new PayoutsView(database.Options);
                // Settings constructor queries Credential Manager; reuse prior Settings renders.
                // Here only the sidebar's stable destination is checked, without personal reads.
                default: return new UserControl { Tag = page };
            }
        }
        window = new MainWindow(Create) { Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
        var content = (ContentControl)window.FindName("MainContent");
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            switch (content.Content)
            {
                case DashboardView dashboard: await dashboard.LoadDashboardData(); break;
                case SessionsView sessions: await sessions.RefreshSessionsAsync(); await sessions.EvidenceCompletion; break;
                case StatisticsView statistics: await statistics.LoadStatistics(); break;
                case PayoutsView payouts: await payouts.RefreshCompletion; break;
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        }
        try
        {
            window.Show(); await Settle();
            var first = (DashboardView)content.Content;
            Assert.Equal(.2345m, first.PeriodEarned);
            Assert.Equal(0, first.Balance); Assert.Contains("without posted dates", first.BalanceDescription);
            await Capture(window, $"shell-dashboard-{width}");
            Descendants<Button>(first).Single(button => Equals(button.Content, "Sessions →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            var sessions = Assert.IsType<SessionsView>(content.Content);
            var row = Assert.Single(((SessionsViewModel)sessions.DataContext).Sessions);
            sessions.SelectSession(row); await sessions.EvidenceCompletion;
            row.BeginEdit(); row.DraftEarnings = "3.4567";
            window.NavigateTo(NavigationPage.Dashboard);
            Assert.Same(sessions, content.Content); Assert.True(window.IsEditorOpen);
            await Capture(window, $"shell-draft-{width}");
            Descendants<Button>(sessions).Single(button => Equals(button.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Settle();
            Assert.False(window.IsEditorOpen);
            using (var db = database.CreateContext()) { Assert.Equal(3.4567m, db.Sessions.Single().Earnings); Assert.Single(db.SessionChanges); }
            window.NavigateTo(NavigationPage.Dashboard); await Settle();
            var refreshed = (DashboardView)content.Content;
            Assert.NotSame(first, refreshed); Assert.Equal(2.4567m, refreshed.PeriodEarned);
            // Existing manual-save rules fill a missing verified posted date from recording date.
            Assert.Equal(3.4567m, refreshed.Balance);
            window.NavigateTo(NavigationPage.Statistics); await Settle();
            var statistics = (StatisticsView)content.Content;
            Assert.Equal(refreshed.TodayEarnings, statistics.TodayEarnings);
            Assert.Equal(refreshed.PeriodEarned, statistics.TrendPeriods.Sum(period => period.TotalEarnings));
            await Capture(window, $"shell-statistics-{width}");
            window.NavigateTo(NavigationPage.Payments); await Settle();
            Assert.Equal("$2.46", ((TextBlock)((PayoutsView)content.Content).FindName("TotalEarnedValue")).Text);
            await Capture(window, $"shell-payments-{width}");
            window.NavigateTo(NavigationPage.Games); await Settle();
            var games = (GamesView)content.Content; var model = (GamesViewModel)games.DataContext;
            model.GamePassOnly = true; Assert.Single(model.GamesView.Cast<GameItemViewModel>());
            model.GamePassOnly = false; model.PlayableOnly = true; Assert.Empty(model.GamesView.Cast<GameItemViewModel>());
            var game = Assert.Single(model.Games); game.ToggleOwnedCommand.Execute(null);
            Assert.Single(model.GamesView.Cast<GameItemViewModel>());
            await Capture(window, $"shell-games-{width}");
            window.OpenGameDetails(game.Game); await Settle();
            Assert.Equal(1, games.SelectedGame!.Id);
            await Capture(window, $"shell-details-{width}");
            window.NavigateTo(NavigationPage.Settings); await Settle();
            Assert.Equal(NavigationPage.Settings, ((UserControl)content.Content).Tag);
            window.NavigateTo(NavigationPage.Games); await Settle();
            Assert.True(Assert.Single(((GamesViewModel)((GamesView)content.Content).DataContext).Games).IsOwned);
            window.NavigateTo(NavigationPage.Sessions); await Settle();
            Assert.Equal(3.4567m, Assert.Single(((SessionsViewModel)((SessionsView)content.Content).DataContext).Sessions).Session.Earnings);
            window.NavigateTo(NavigationPage.Dashboard); await Settle();
            var offline = (DashboardView)content.Content; var trend = offline.Trend;
            using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE Payouts RENAME TO UnavailablePayouts");
            await offline.LoadDashboardData();
            Assert.Same(trend, offline.Trend); Assert.Contains("out of date", ((TextBlock)offline.FindName("LoadStatusText")).Text);
            await Capture(window, $"shell-stale-{width}");
            using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE UnavailablePayouts RENAME TO Payouts");
            await offline.LoadDashboardData();
            Assert.Equal(2.4567m, offline.PeriodEarned);
            using var read = database.CreateContext();
            Assert.Single(read.SessionChanges); Assert.Equal(123.4567m, read.GameOwnership.Single().PurchaseAmount);
            Assert.Equal(1.2345m, System.Text.Json.JsonDocument.Parse(read.SessionChanges.Single().BeforeValuesJson).RootElement.GetProperty("Earnings").GetDecimal());
            Assert.Empty(read.GameChanges);
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T found) yield return found; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task Capture(FrameworkElement element, string name)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); element.UpdateLayout();
        var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "UiReleaseVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
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
