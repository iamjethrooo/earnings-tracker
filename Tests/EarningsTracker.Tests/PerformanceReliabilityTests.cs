using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.ViewModels;
using System.Data.Common;
using System.Collections.Specialized;
using EarningsTracker.Views;
using EarningsTracker.Enums;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EarningsTracker.Views.Modals;
using EarningsTracker.Controls;
using EarningsTracker.Utilities;
using System.Windows.Media.Imaging;
using System.IO;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class PerformanceReliabilityTests(WpfTestHost host)
{
    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Statistics")]
    [InlineData("Payouts")]
    [InlineData("Sessions")]
    public Task WorkerSaveRefreshesLoadedPagesOnTheirDispatcher(string page) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Session session;
        using (var db = database.CreateContext())
        {
            var game = new Game { Name = "Example", RatePerHour = 1 };
            db.Games.Add(game); db.SaveChanges();
            session = new Session { GameId = game.Id, GameName = game.Name, Date = DateTime.Today,
                RecordedDate = DateTime.Today, PostedDate = DateTime.Today, Status = SessionStatus.Verified, Earnings = 1 };
            db.Sessions.Add(session); db.SaveChanges();
        }
        UserControl view = page switch
        {
            "Dashboard" => new DashboardView(database.Options),
            "Statistics" => new StatisticsView(database.Options),
            "Payouts" => new PayoutsView(database.Options),
            _ => new SessionsView(database.Options)
        };
        var window = new Window { Content = view, Width = 1000, Height = 720, Left = -10000, Top = -10000, ShowInTaskbar = false };
        async Task Settle()
        {
            switch (view)
            {
                case PayoutsView payments: await payments.RefreshCompletion; break;
                case SessionsView sessions: await ((SessionsViewModel)sessions.DataContext).LoadSessionsAsync(); break;
                case DashboardView dashboard: await dashboard.LoadDashboardData(); break;
                case StatisticsView statistics: await statistics.LoadStatistics(); break;
            }
        }
        try
        {
            window.Show(); await Settle();
            session.Earnings = 2;
            await Task.Run(() => SessionService.Update(session.Id, session, database.Options));
            await Settle();
            switch (view)
            {
                case DashboardView dashboard: Assert.Equal(2m, dashboard.TotalEarned); break;
                case StatisticsView statistics: Assert.Equal(2m, statistics.TodayEarnings); break;
                case PayoutsView payments: Assert.Equal("$2.00", ((TextBlock)payments.FindName("TotalEarnedValue")).Text); break;
                case SessionsView sessions: Assert.Equal(2m, ((SessionsViewModel)sessions.DataContext).Sessions.Single().Session.Earnings); break;
            }
            using var read = database.CreateContext();
            Assert.Equal(2m, read.Sessions.Single().Earnings); Assert.Single(read.SessionChanges);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SessionsRefreshRetainsSelectedRowAndDraftGameInRealEditor() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example", RatePerHour = 1 });
            db.Sessions.Add(new Session { GameName = "Unlinked", RecordedDate = DateTime.Today, Date = DateTime.Today });
            db.SaveChanges();
        }
        var view = new SessionsView(database.Options) { Width = 940, Height = 600 };
        var model = (SessionsViewModel)view.DataContext;
        await model.LoadSessionsAsync();
        var row = model.Sessions.Single(); row.BeginEdit();
        view.Measure(new Size(940, 600)); view.Arrange(new Rect(0, 0, 940, 600)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var combo = VisualDescendants<ComboBox>(view).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == "Session game");
        combo.SelectedItem = model.AvailableGames.Single();
        var gameId = row.DraftGame!.Id;
        var list = (ListBox)view.FindName("SessionsList"); list.SelectedItem = row;
        await model.LoadSessionsAsync();
        view.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Same(row, list.SelectedItem); Assert.True(row.IsEditing);
        Assert.Equal(gameId, row.DraftGame?.Id); Assert.Same(model.AvailableGames.Single(), row.DraftGame);
        var refreshed = VisualDescendants<ComboBox>(view).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == "Session game");
        Assert.Same(row.DraftGame, refreshed.SelectedItem);
        using var read = database.CreateContext();
        Assert.Null(read.Sessions.Single().GameId); Assert.Empty(read.SessionChanges);
    });

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualDescendants<T>(child)) yield return nested;
        }
    }

    [Fact]
    public Task ApplicationWindowCannotCloseDuringAnImportCommit() => host.RunAsync(async () =>
    {
        // Replace the financial page before showing the shell; no production load can run.
        var window = new MainWindow { Left = -10000, Top = -10000, ShowInTaskbar = false };
        ((ContentControl)window.FindName("MainContent")).Content = new Border();
        var modal = (Modal)window.FindName("MainModal");
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            modal.IsBusy = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(((Button)window.FindName("WindowCloseButton")).IsEnabled);
            window.Close(); Assert.False(closed);
            modal.IsBusy = false; window.Close(); Assert.True(closed);
        }
        finally { modal.IsBusy = false; if (!closed) window.Close(); }
    });

    [Fact]
    public Task StartupProgressRemainsVisibleUntilInitializationFinishes() => host.RunAsync(async () =>
    {
        var window = new StartupWindow { WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000, ShowInTaskbar = false };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(window.FindResource("DefaultBackgroundColor"), window.Background);
            Capture(window, "startup-progress.png");
            window.Close(); Assert.False(closed);
            window.InitializationFinished = true; window.Close(); Assert.True(closed);
        }
        finally { window.InitializationFinished = true; if (!closed) window.Close(); }
    });

    private static void Capture(FrameworkElement element, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Phase3Visuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }

    [Fact]
    public Task TrendValuesUseChartPeriodsAndResizeRenderingIsDeferredUntilLatestSize() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var today = new DateTime(2026, 6, 5);
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", Date = today, RecordedDate = today,
                PostedDate = today, Status = SessionStatus.Verified, Earnings = 2.5m, Duration = TimeSpan.FromHours(1) });
            db.Payouts.Add(new Payout { Date = today, Type = "Earnings correction", Status = "Verified", Amount = -0.5m, ReconciliationState = "Separate" });
            db.SaveChanges();
        }
        var view = new StatisticsView(database.Options) { Width = 1000, Height = 720 };
        await view.LoadStatistics(asOfDate: today);
        view.Measure(new Size(1000, 720)); view.Arrange(new Rect(0, 0, 1000, 720)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var canvas = (Canvas)view.FindName("EarningsTrendCanvas");
        var before = canvas.Children.OfType<System.Windows.Shapes.Line>().First();
        foreach (var width in new[] { 900d, 850, 740 })
        {
            view.Width = width;
            view.Measure(new Size(width, 720)); view.Arrange(new Rect(0, 0, width, 720)); view.UpdateLayout();
        }
        Assert.Same(before, canvas.Children.OfType<System.Windows.Shapes.Line>().First());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var after = canvas.Children.OfType<System.Windows.Shapes.Line>().First();
        Assert.NotSame(before, after); Assert.Equal(canvas.ActualWidth - 8, after.X2);
        Assert.Same(before.Stroke, after.Stroke); Assert.True(after.Stroke.IsFrozen);
        var expander = (Expander)view.FindName("TrendValuesExpander");
        Assert.False(expander.IsExpanded);
        expander.IsExpanded = true; view.UpdateLayout();
        var values = (ListBox)view.FindName("TrendValuesList");
        var period = view.TrendPeriods.Single(item => item.StartDate == today);
        Assert.Equal(2m, period.TotalEarnings); Assert.Equal(1m, period.SessionHours);
        Assert.Same(period, values.Items.Cast<EarningsPeriodItem>().Single(item => item.StartDate == today));
        // Host the disclosure in a native window so its deferred template and keyboard focus are active.
        ((Grid)VisualTreeHelper.GetParent(expander)).Children.Remove(expander);
        expander.DataContext = view;
        var window = new Window { Content = expander, Width = 650, Height = 300, Left = -10000, Top = -10000, ShowInTaskbar = false,
            Background = (Brush)view.FindResource("DefaultBackgroundColor") };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            values.ScrollIntoView(period); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var container = (ListBoxItem)values.ItemContainerGenerator.ContainerFromItem(period);
            Assert.NotNull(container);
            System.Windows.Input.Keyboard.Focus(container);
            Assert.Same(container, System.Windows.Input.Keyboard.FocusedElement);
            Assert.Equal(period.AccessibleSummary, System.Windows.Automation.AutomationProperties.GetName(container));
            Assert.Contains("$2.00", period.AccessibleSummary);
            Capture(expander, "trend-values.png");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DecodedArtworkCacheEvictsLeastRecentImagesAndHonorsByteLimit() => host.RunAsync(() =>
    {
        static BitmapSource Image(int size, bool freeze = true)
        {
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, new byte[size * size * 4], size * 4);
            if (freeze) bitmap.Freeze();
            return bitmap;
        }
        var cache = new DecodedArtworkCache(2, 4096);
        var a = Image(16); var b = Image(16); var c = Image(16);
        cache.Add("a", a); cache.Add("b", b);
        Assert.Same(a, cache.Get("a"));
        cache.Add("c", c);
        Assert.Null(cache.Get("b")); Assert.Same(a, cache.Get("a")); Assert.Same(c, cache.Get("c"));
        cache.Add("large", Image(100));
        Assert.Null(cache.Get("large")); Assert.Equal(2048, cache.RetainedBytes);
        Assert.Throws<ArgumentException>(() => cache.Add("unfrozen", Image(16, false)));
        var small = new DecodedArtworkCache(128, 1500);
        small.Add("a", a); small.Add("b", b);
        Assert.Null(small.Get("a")); Assert.Same(b, small.Get("b")); Assert.Equal(1024, small.RetainedBytes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ArtworkIsSharedAndUnchangedReloadsRetainPendingAndDisplayedCovers() => host.RunAsync(async () =>
    {
        var catalog = CatalogService.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));
        var entry = catalog.Games.First(x => x.CoverUrl != "");
        GameMetadata Metadata() => new() { GameId = 1, CoverUrl = entry.CoverUrl, CoverSha256 = entry.CoverSha256 };
        var item = new GameItemViewModel(new Game { Id = 1, Name = "Example" }, false);
        var changes = new List<string?>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        item.SetCatalog(Metadata(), [], [], [], null, new Dictionary<string, ExchangeRate>());
        _ = item.Cover;
        // Replace the metadata instance while the original request may still be pending.
        item.SetCatalog(Metadata(), [], [], [], null, new Dictionary<string, ExchangeRate>());
        for (var i = 0; i < 80 && item.Cover == null; i++) await Task.Delay(25);
        var cover = Assert.IsAssignableFrom<BitmapSource>(item.Cover);
        Assert.True(cover.IsFrozen);
        var shared = await CatalogArtwork.LoadAsync(entry.CoverUrl, entry.CoverSha256.ToUpperInvariant());
        Assert.Same(cover, shared);
        changes.Clear();
        item.SetCatalog(Metadata(), [], [], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Same(cover, item.Cover);
        Assert.DoesNotContain(nameof(GameItemViewModel.Cover), changes);
        Assert.DoesNotContain("", changes);
        var row = new SessionItemViewModel(new Session { GameId = 1 });
        row.SetArtwork(Metadata()); Assert.Same(cover, row.Cover);
        row.SetArtwork(Metadata()); Assert.Same(cover, row.Cover);
        row.SetArtwork(null); Assert.Null(row.Cover);
        item.SetCatalog(null, [], [], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Null(item.Cover);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StartupKeepsDispatcherResponsiveAndOpensOnlyAfterSuccessfulInitialization(bool fail) => host.RunAsync(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = Environment.CurrentManagedThreadId;
        var opened = false;
        var task = App.InitializeBeforeOpeningAsync(() =>
        {
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            entered.TrySetResult(); resume.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (fail) throw new InvalidOperationException("Initialization failed");
        }, () => { Assert.Equal(uiThread, Environment.CurrentManagedThreadId); opened = true; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Input);
            Assert.False(opened);
        }
        finally { resume.TrySetResult(); }
        if (fail) { await Assert.ThrowsAsync<InvalidOperationException>(() => task); Assert.False(opened); }
        else { await task; Assert.True(opened); }
    });

    [Theory]
    [InlineData("Sessions")]
    [InlineData("Selected game")]
    [InlineData("Payouts")]
    public Task ClosingWhilePreviewRunsSuppressesLatePublicationAndReleasesContexts(string kind) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Example", RatePerHour = 1 };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        var gate = new PauseRead(kind == "Payouts" ? "Payouts" : "Games");
        var disposals = new System.Collections.Concurrent.ConcurrentBag<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate)
            .LogTo(disposals.Add, [CoreEventId.ContextDisposed]).Options;
        UserControl view = kind switch
        {
            "Payouts" => new ImportPayoutsView(options),
            "Selected game" => new AddSessionView(game, options),
            _ => new ImportSessionsView(options)
        };
        var modal = new Modal { ModalContent = view, Width = 740, Height = 560 };
        modal.Measure(new Size(740, 560)); modal.Arrange(new Rect(0, 0, 740, 560)); modal.UpdateLayout();
        ((TextBox)view.FindName("ImportTextBox")).Text = kind == "Payouts" ? "|6/5/2026|—|Payment|Example|Paid|-$1.00|"
            : kind == "Selected game" ? "recording-20260605120000123.mkv\n6/5/2026 · 1m\n+$1.00\nVerified"
            : "Example\nvideo.mkv · 6/5/2026 12:00 PM · 1m\n+$1.00\nVerified";
        Click(view, "Parse Import");
        var task = Completion(view);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.NotEqual(Environment.CurrentManagedThreadId, gate.ThreadId);
            Assert.False(view.IsEnabled);
            Assert.Equal(Visibility.Visible, ((TextBlock)view.FindName("BusyStatus")).Visibility);
            await Dispatcher.Yield(DispatcherPriority.Input);
            Click(view, "Parse Import"); Assert.Same(task, Completion(view));
            modal.Close(); Assert.Null(modal.ModalContent);
            Assert.Empty(disposals); // The paused context is still owned by its operation.
        }
        finally { gate.Resume.TrySetResult(); }
        await task;
        Assert.Equal(kind == "Payouts" ? 1 : 2, disposals.Count);
        Assert.Equal(Visibility.Collapsed, ((Grid)view.FindName("ImportResultContainer")).Visibility);
        using var read = database.CreateContext();
        Assert.Empty(read.Payouts); Assert.Empty(read.Sessions);
        Assert.Empty(read.PayoutChanges); Assert.Empty(read.SessionChanges);
    });

    private static Task Completion(UserControl view) => view is ImportPayoutsView payouts
        ? payouts.OperationCompletion : SessionCompletion(view);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PayoutImportGuardsCommitAndDoesNotResubmitAfterSavedWarning(bool failRefresh) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var gate = new PauseSave();
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate).Options;
        var view = new ImportPayoutsView(options);
        var modal = new Modal { ModalContent = view, Width = 740, Height = 560 };
        modal.Measure(new Size(740, 560)); modal.Arrange(new Rect(0, 0, 740, 560)); modal.UpdateLayout();
        ((TextBox)view.FindName("ImportTextBox")).Text = "|6/5/2026|—|Payment|Example|Paid|-$1.00|";
        Click(view, "Parse Import"); await view.OperationCompletion;
        EventHandler failure = (_, _) => throw new InvalidOperationException("Test refresh failure");
        if (failRefresh) PayoutService.DataChanged += failure;
        try
        {
            Click(view, "Import"); var task = view.OperationCompletion;
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                Assert.True(modal.IsBusy); Assert.False(view.IsEnabled);
                Click(view, "Import"); Assert.Same(task, view.OperationCompletion);
                modal.Close(); Assert.Same(view, modal.ModalContent);
                await Dispatcher.Yield(DispatcherPriority.Input);
            }
            finally { gate.Resume.TrySetResult(); }
            await task;
            Assert.False(modal.IsBusy); Assert.True(view.IsEnabled);
            Assert.Equal("Done", ((Button)view.FindName("ConfirmImportButton")).Content);
            if (failRefresh) Assert.Contains("saved", ((TextBlock)view.FindName("ImportOutcome")).Text, StringComparison.OrdinalIgnoreCase);
            Click(view, "Done"); await view.OperationCompletion;
            using var read = database.CreateContext();
            Assert.Single(read.Payouts); Assert.Single(read.PayoutChanges);
        }
        finally { PayoutService.DataChanged -= failure; modal.Close(); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task SessionImportGuardsBusyCommitAndPreservesSavedWarning(bool selectedGame, bool failRefresh) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Example", RatePerHour = 1 };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        var gate = new PauseSave();
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate).Options;
        UserControl view = selectedGame ? new AddSessionView(game, options) : new ImportSessionsView(options);
        var modal = new Modal { ModalContent = view, Width = 740, Height = 560 };
        modal.Measure(new Size(740, 560)); modal.Arrange(new Rect(0, 0, 740, 560)); modal.UpdateLayout();
        ((TextBox)view.FindName("ImportTextBox")).Text = selectedGame
            ? "recording-20260605120000123.mkv\n6/5/2026 · 1m\n+$1.00\nVerified"
            : "Example\nvideo.mkv · 6/5/2026 12:00 PM · 1m\n+$1.00\nVerified";
        Click(view, "Parse Import"); await SessionCompletion(view);
        EventHandler failure = (_, _) => throw new InvalidOperationException("Test refresh failure");
        if (failRefresh) PayoutService.DataChanged += failure;
        try
        {
            Click(view, "Import Sessions");
            var completion = SessionCompletion(view);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                Assert.False(view.IsEnabled);
                Assert.True(modal.IsBusy);
                await Dispatcher.Yield(DispatcherPriority.Input);
                if (!selectedGame && !failRefresh) { modal.UpdateLayout(); Capture(modal, "session-import-busy.png"); }
                Click(view, "Import Sessions");
                Assert.Same(completion, SessionCompletion(view));
                modal.Close();
                Assert.Same(view, modal.ModalContent);
            }
            finally { gate.Resume.TrySetResult(); }
            await completion;
            Assert.False(modal.IsBusy);
            Assert.True(view.IsEnabled);
            var summary = (ImportResultModal)view.FindName("ImportResultModal");
            Assert.Equal(failRefresh ? "Import Saved — Display Update Failed" : "Import Complete",
                ((TextBlock)summary.FindName("ResultTitle")).Text);
            Click(view, "Import Sessions"); await SessionCompletion(view);
            using var read = database.CreateContext();
            Assert.Single(read.Sessions); Assert.Single(read.SessionChanges);
        }
        finally { PayoutService.DataChanged -= failure; modal.Close(); }
    });

    private static Task SessionCompletion(UserControl view) => view is AddSessionView selected
        ? selected.OperationCompletion : ((ImportSessionsView)view).OperationCompletion;
    private static void Click(DependencyObject parent, string label)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is Button button && button.Content as string == label)
            { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
            if (HasButton(child, label)) { Click(child, label); return; }
        }
        throw new InvalidOperationException($"Button not found: {label}");
    }
    private static bool HasButton(DependencyObject parent, string label) =>
        LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().Any(child =>
            child is Button button && button.Content as string == label || HasButton(child, label));

    private sealed class PauseSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Entered.TrySetResult();
            Resume.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return result;
        }
    }

    [Fact]
    public Task DeferredGamesLoadReadsOnWorkerAndCanRetryWithoutPublishingPartialData() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example" }); db.SaveChanges();
        }
        var gate = new PauseRead("Games");
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate).Options;
        var model = new GamesViewModel(options, loadImmediately: false);
        Assert.Empty(model.Games);
        var task = model.LoadGamesAsync();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.True(model.IsLoading);
            Assert.NotEqual(Environment.CurrentManagedThreadId, gate.ThreadId);
            Assert.Same(task, model.LoadGamesAsync());
            await Dispatcher.Yield(DispatcherPriority.Input);
        }
        finally { gate.Resume.TrySetResult(); }
        await task;
        Assert.True(model.HasLoaded);
        Assert.Single(model.Games);
        var row = model.Games.Single();
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO UnavailableMetadata");
        await model.LoadGamesAsync();
        Assert.Same(row, model.Games.Single());
        Assert.Contains("Could not load", model.CatalogStatus);
        Assert.False(model.IsLoading);
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE UnavailableMetadata RENAME TO GameMetadata");
        await model.LoadGamesAsync();
        Assert.DoesNotContain("Could not load", model.CatalogStatus);
        Assert.Single(model.Games);
    });

    [Fact]
    public Task PaymentReadLeavesDispatcherResponsiveAndCoalescesRefreshes() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Payouts.Add(new Payout { Date = DateTime.Today, Type = "Payment", Status = "Paid", Amount = -1 });
            db.SaveChanges();
        }
        var gate = new PauseRead("Payouts");
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate).Options;
        var view = new PayoutsView(options);
        var resets = 0;
        view.History.CollectionChanged += (_, _) => resets++;
        var task = view.RefreshHistory();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.NotEqual(Environment.CurrentManagedThreadId, gate.ThreadId);
            await Dispatcher.Yield(DispatcherPriority.Input);
            Assert.False(((Button)view.FindName("RetryLoadButton")).IsEnabled);
            Assert.Same(task, view.RefreshHistory(changesSaved: true));
        }
        finally { gate.Resume.TrySetResult(); }
        await task;
        Assert.Equal(2, gate.Reads);
        Assert.Equal(1, resets);
        Assert.Single(view.History);
        Assert.True(((Button)view.FindName("RetryLoadButton")).IsEnabled);
    });

    internal sealed class PauseRead(string table) : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }
        public int ThreadId { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains($"FROM \"{table}\""))
            {
                Reads++;
                if (Reads == 1)
                {
                    ThreadId = Environment.CurrentManagedThreadId;
                    Entered.TrySetResult();
                    Resume.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                }
            }
            return result;
        }
    }

    [Fact]
    public Task LargeGameHistoryKeepsFewContainersAndSupportsFilteringAndEndScroll() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Large history" };
        using (var db = database.CreateContext())
        {
            db.Games.Add(game); db.SaveChanges();
            db.Sessions.AddRange(Enumerable.Range(0, 10000).Select(i => new Session
            {
                GameId = game.Id, GameName = game.Name, RecordedDate = DateTime.Today.AddMinutes(-i),
                Date = DateTime.Today.AddMinutes(-i), Status = i % 2 == 0 ? SessionStatus.Verified : SessionStatus.Pending
            }));
            db.SaveChanges();
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var view = new GamesView(database.Options) { Width = 740, Height = 560 };
        view.OpenGameDetails(game);
        view.Measure(new Size(740, 560)); view.Arrange(new Rect(0, 0, 740, 560)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)view.FindName("GameSessionsList");
        Assert.Equal(10001, list.Items.Count);
        Assert.InRange(CountContainers(list), 1, 100);
        var scroll = view.DetailsScroll!;
        scroll.ScrollToEnd(); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(10000));
        Assert.InRange(CountContainers(list), 1, 100);
        var details = (Grid)view.FindName("GameDetailsView");
        ((GameDetailsViewModel)details.DataContext).FilterSessions(SessionStatus.Verified);
        view.UpdateLayout();
        scroll.ScrollToEnd(); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(5001, list.Items.Count);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(5000));
        Assert.InRange(CountContainers(list), 1, 100);
        scroll.ScrollToTop(); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
        System.Diagnostics.Trace.WriteLine($"10,000-session detail layout/filter/end-scroll: {timer.ElapsedMilliseconds} ms, {CountContainers(list)} realized containers.");
        ((Button)view.FindName("DetailsBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(list.Items.Cast<object>()); // Leaving details releases the retained history view.
    });

    private static int CountContainers(DependencyObject parent)
    {
        var count = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBoxItem && parent is VirtualizingPanel) count++;
            count += CountContainers(child);
        }
        return count;
    }

    [Fact]
    public Task SessionsCoalesceReloadsAndPublishLatestSnapshotOnce() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Example", RecordedDate = DateTime.Today, Date = DateTime.Today });
            db.SaveChanges();
        }
        var gate = new PauseMetadataRead();
        var options = new DbContextOptionsBuilder<AppDbContext>(database.Options).AddInterceptors(gate).Options;
        var model = new SessionsViewModel(options);
        var resets = 0;
        model.Sessions.CollectionChanged += (_, e) => { Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action); resets++; };
        var first = model.LoadSessionsAsync();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var db = database.CreateContext();
            db.Sessions.Single().GameName = "Latest";
            db.SaveChanges();
            Assert.Same(first, model.LoadSessionsAsync());
            Assert.Same(first, model.LoadSessionsAsync());
            Assert.True(model.IsLoading);
        }
        finally { gate.Resume.TrySetResult(); }
        await first;
        Assert.False(model.IsLoading);
        Assert.Null(model.LoadError);
        Assert.Equal("Latest", model.Sessions.Single().Session.GameName);
        Assert.Equal(1, resets);
        Assert.Equal(2, gate.Reads);
        var row = model.Sessions.Single();
        row.BeginEdit();
        row.DraftEarnings = "12.34";
        model.SearchText = "Latest";
        await model.LoadSessionsAsync();
        Assert.Same(row, model.Sessions.Single());
        Assert.True(row.IsEditing);
        Assert.Equal("12.34", row.DraftEarnings);
        Assert.Single(model.SessionsView.Cast<SessionItemViewModel>());
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("DROP TABLE GameMetadata");
        await model.LoadSessionsAsync();
        Assert.NotNull(model.LoadError);
        Assert.Same(row, model.Sessions.Single());
        Assert.False(model.IsLoading);
    });

    private sealed class PauseMetadataRead : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"GameMetadata\""))
            {
                Reads++;
                if (Reads == 1)
                {
                    Entered.TrySetResult();
                    await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            return result;
        }
    }
}
