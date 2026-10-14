using System.Windows.Threading;
using System.IO;
using EarningsTracker.Utilities;
using EarningsTracker.Views;
using Velopack;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class ApplicationUpdateTests(WpfTestHost host)
{
    [Fact]
    public Task RealInlineSessionDraftBlocksCoordinatorRestart() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var sessions = new SessionsView(database.Options);
        var model = Assert.IsType<EarningsTracker.ViewModels.SessionsViewModel>(sessions.DataContext);
        var item = new EarningsTracker.ViewModels.SessionItemViewModel(new EarningsTracker.Models.Session { Date = DateTime.Today });
        model.Sessions.Add(item);
        var window = new MainWindow(page => page == EarningsTracker.Enums.NavigationPage.Sessions ? sessions : new System.Windows.Controls.UserControl());
        window.NavigateTo(EarningsTracker.Enums.NavigationPage.Sessions);
        using var service = Service(new Manager { Pending = Asset }, editor: () => window.IsEditorOpen);
        item.BeginEdit(); Assert.True(window.IsEditorOpen); Assert.False(service.Restart());
        item.CancelEdit(); Assert.False(window.IsEditorOpen); Assert.True(service.CanRestart);
        window.Close(); sessions.DataContext = null;
        return Task.CompletedTask;
    });
    [Fact]
    public Task OpenEditorAndDisposedPreviewWorkStillBlockRestart() => host.RunAsync(async () =>
    {
        var owner = new System.Windows.Controls.UserControl();
        var status = new System.Windows.Controls.TextBlock();
        using var work = new EarningsTracker.Controls.DialogWork(owner, status);
        using var finish = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = Service(new Manager { Pending = Asset }, UpdateRestartGuard.Shared);
        var operation = work.RunAsync("Parsing", () => { started.SetResult(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return 1; });
        try
        {
            await started.Task; work.Dispose();
            Assert.True(UpdateRestartGuard.Shared.IsBusy); Assert.False(service.Restart());
        }
        finally { finish.Set(); await operation; }
    });

    [Fact]
    public Task LiveSqliteContextBlocksRestartUntilDisposed() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        _ = db.Games.Count();
        using var service = Service(new Manager { Pending = Asset }, UpdateRestartGuard.Shared);
        Assert.True(UpdateRestartGuard.Shared.IsBusy); Assert.False(service.Restart());
        return Task.CompletedTask;
    });

    [Fact]
    public Task NotificationsArePublishedOnUiDispatcher() => host.RunAsync(async () =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var guard = new UpdateRestartGuard(); using var service = Service(new Manager(), guard);
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.PropertyChanged += (_, _) => { Assert.True(dispatcher.CheckAccess()); notified.TrySetResult(); };
        await Task.Run(() => { using var work = guard.Enter(); });
        await notified.Task;
    });
    private static readonly VelopackAsset Asset = new() { Version = Velopack.SemanticVersion.Parse("1.0.1"), FileName = "test-full.nupkg", Type = VelopackAssetType.Full };
    private sealed class Manager : UpdateManager
    {
        internal bool Installed = true;
        internal VelopackAsset? Pending;
        internal bool BrokenPending;
        internal Func<Task<UpdateInfo?>> Check = () => Task.FromResult<UpdateInfo?>(new UpdateInfo(Asset, false, null, []));
        internal Func<Action<int>?, CancellationToken, Task>? Download;
        internal int Checks;
        internal Manager() : base(".", locator: new Velopack.Locators.TestVelopackLocator("Test.Tracker", "1.0.0", Path.GetTempPath())) { }
        public override bool IsInstalled => Installed;
        public override VelopackAsset? UpdatePendingRestart => BrokenPending ? throw new IOException("cache unreadable") : Pending;
        public override Task<UpdateInfo?> CheckForUpdatesAsync() { Checks++; return Check(); }
        public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            if (Download != null) await Download(progress, cancelToken);
            else { progress?.Invoke(73); Pending = Asset; }
        }
    }
    private static ApplicationUpdates Service(Manager manager, UpdateRestartGuard? guard = null, Func<bool>? editor = null,
        Func<Action>? cleanup = null, Action? shutdown = null, Action<VelopackAsset>? arm = null) =>
        new(manager, Dispatcher.CurrentDispatcher, guard ?? new(), editor ?? (() => false),
            cleanup ?? (() => () => { }), shutdown ?? (() => { }), arm ?? (_ => { }));

    [Fact]
    public Task UnpackagedStartupDoesNotCheckOrRestart() => host.RunAsync(async () =>
    {
        var manager = new Manager { Installed = false };
        using var service = Service(manager);
        await service.CheckOnStartupAsync(true); await service.CheckAsync(); await service.DownloadAsync();
        Assert.Equal(ApplicationUpdateState.Unavailable, service.State);
        Assert.Equal(0, manager.Checks); Assert.False(service.Restart());
    });

    [Fact]
    public Task StartupChecksOnceAndNeverDownloads() => host.RunAsync(async () =>
    {
        var manager = new Manager(); using var service = Service(manager);
        await service.CheckOnStartupAsync(true); await service.CheckOnStartupAsync(true);
        Assert.Equal(1, manager.Checks); Assert.Equal(ApplicationUpdateState.Available, service.State);
        Assert.Null(manager.Pending); Assert.True(service.HasNotice);
    });

    [Fact]
    public Task DisabledStartupStillAllowsManualCheckAndRetry() => host.RunAsync(async () =>
    {
        var manager = new Manager { Check = () => throw new IOException("offline") };
        using var service = Service(manager); await service.CheckOnStartupAsync(false);
        Assert.Equal(0, manager.Checks); await service.CheckAsync();
        Assert.Equal(ApplicationUpdateState.Error, service.State); Assert.True(service.CanCheck);
        manager.Check = () => Task.FromResult<UpdateInfo?>(null); await service.CheckAsync();
        Assert.Equal(ApplicationUpdateState.UpToDate, service.State);
    });

    [Fact]
    public Task UnreadableUpdateCacheDoesNotBlockApplicationUse() => host.RunAsync(async () =>
    {
        var manager = new Manager { BrokenPending = true }; using var service = Service(manager);
        await service.CheckOnStartupAsync(true);
        Assert.Equal(ApplicationUpdateState.Unavailable, service.State); Assert.Equal(0, manager.Checks);
        Assert.False(service.CanRestart); Assert.Contains("saved data remains usable", service.Message);
    });

    [Fact]
    public Task FailedRecheckRetainsKnownUpdateAndDownloadAction() => host.RunAsync(async () =>
    {
        var manager = new Manager(); using var service = Service(manager); await service.CheckAsync();
        manager.Check = () => throw new IOException("offline"); await service.CheckAsync();
        Assert.Equal(ApplicationUpdateState.Error, service.State); Assert.Equal("1.0.1", service.Version);
        Assert.True(service.HasNotice); Assert.True(service.CanDownload);
    });

    [Fact]
    public Task CancelledCheckDiscardsLateResultAndRemainsSerialized() => host.RunAsync(async () =>
    {
        var result = new TaskCompletionSource<UpdateInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new Manager { Check = () => result.Task }; using var service = Service(manager);
        var check = service.CheckAsync(); service.Cancel(); await service.CheckAsync();
        Assert.Equal(ApplicationUpdateState.Checking, service.State);
        result.SetResult(new UpdateInfo(Asset, false, null, [])); await check;
        Assert.Equal(ApplicationUpdateState.Idle, service.State); Assert.False(service.CanDownload);
        Assert.Equal(1, manager.Checks);
    });

    [Fact]
    public Task DownloadCancellationProgressAndRetryUseSdkToken() => host.RunAsync(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new Manager { Download = async (progress, token) => { progress?.Invoke(42); started.SetResult(); await Task.Delay(Timeout.Infinite, token); } };
        using var service = Service(manager); await service.CheckAsync();
        var download = service.DownloadAsync(); await started.Task;
        Assert.False(service.CanCheck); Assert.True(service.CanCancel);
        service.Cancel(); await download;
        Assert.Equal(ApplicationUpdateState.Available, service.State); Assert.True(service.CanDownload);
        manager.Download = null; await service.DownloadAsync();
        Assert.Equal(ApplicationUpdateState.ReadyToRestart, service.State); Assert.Equal(100, service.Progress);
    });

    [Fact]
    public Task FailedDownloadKeepsAvailableVersionForRetry() => host.RunAsync(async () =>
    {
        var manager = new Manager { Download = (_, _) => throw new IOException("checksum or disk failure") };
        using var service = Service(manager); await service.CheckAsync(); await service.DownloadAsync();
        Assert.Equal(ApplicationUpdateState.Error, service.State); Assert.Equal("1.0.1", service.Version);
        Assert.True(service.CanDownload); Assert.False(service.CanRestart);
    });

    [Fact]
    public Task PendingDownloadSurvivesServiceRecreationWithoutChecking() => host.RunAsync(async () =>
    {
        var manager = new Manager { Pending = Asset }; using var service = Service(manager);
        await service.CheckOnStartupAsync(true);
        Assert.Equal(ApplicationUpdateState.ReadyToRestart, service.State); Assert.Equal(0, manager.Checks);
    });

    [Fact]
    public Task RestartRechecksEditorsAndWorkNeverQueuesAnExit() => host.RunAsync(() =>
    {
        var guard = new UpdateRestartGuard(); var editor = true; var exits = 0;
        using var service = Service(new Manager { Pending = Asset }, guard, () => editor, shutdown: () => exits++);
        Assert.False(service.Restart()); editor = false;
        using (guard.Enter()) { Assert.False(service.CanRestart); Assert.False(service.Restart()); }
        Assert.Equal(0, exits); Assert.True(service.CanRestart);
        Assert.True(service.Restart()); Assert.Equal(1, exits);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CleanupPrecedesArmingAndShutdownAndPreventsNewWork() => host.RunAsync(() =>
    {
        var order = new List<string>(); var guard = new UpdateRestartGuard();
        using var service = Service(new Manager { Pending = Asset }, guard, cleanup: () =>
        { Assert.Throws<InvalidOperationException>(() => guard.Enter()); order.Add("cleanup"); return () => order.Add("restore"); },
        shutdown: () => order.Add("shutdown"), arm: _ => order.Add("arm"));
        Assert.True(service.Restart()); Assert.Equal(new[] { "cleanup", "arm", "shutdown" }, order);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedArmingRestoresPageAndKeepsReadyState() => host.RunAsync(() =>
    {
        var restored = false; var exits = 0;
        using var service = Service(new Manager { Pending = Asset }, cleanup: () => () => restored = true,
            shutdown: () => exits++, arm: _ => throw new IOException("updater unavailable"));
        Assert.False(service.Restart()); Assert.True(restored); Assert.Equal(0, exits);
        Assert.Equal(ApplicationUpdateState.ReadyToRestart, service.State); Assert.True(service.CanRestart);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisposeCancelsDownloadAndSuppressesLatePublication() => host.RunAsync(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new Manager { Download = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); } };
        var service = Service(manager); await service.CheckAsync(); var task = service.DownloadAsync(); await started.Task;
        var notifications = 0; service.PropertyChanged += (_, _) => notifications++;
        service.Dispose(); await task;
        Assert.Equal(0, notifications); Assert.False(service.CanCheck);
    });

    [Fact]
    public Task SettingsRecreationSharesUpdaterAndKeepsCatalogPreferenceIndependent() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase(); using var service = Service(new Manager());
        var first = new SettingsView(database.Options, service);
        await service.CheckAsync();
        var second = new SettingsView(database.Options, service);
        second.Measure(new System.Windows.Size(900, 900)); second.Arrange(new System.Windows.Rect(0, 0, 900, 900)); second.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var progress = FindProgress(second);
        Assert.Equal(System.Windows.Data.BindingMode.OneWay, System.Windows.Data.BindingOperations.GetBinding(progress, System.Windows.Controls.Primitives.RangeBase.ValueProperty)!.Mode);
        var model = Assert.IsType<EarningsTracker.ViewModels.SettingsViewModel>(second.DataContext);
        Assert.Same(service, model.ApplicationUpdates); Assert.True(service.CanDownload);
        model.SetApplicationStartup(false);
        Assert.True(model.AutomaticUpdates); Assert.False(model.CheckApplicationOnStartup);
        first.DataContext = null; second.DataContext = null;
    });

    private static System.Windows.Controls.ProgressBar FindProgress(System.Windows.DependencyObject root)
    {
        if (root is System.Windows.Controls.ProgressBar bar) return bar;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            try { return FindProgress(System.Windows.Media.VisualTreeHelper.GetChild(root, i)); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Progress bar missing");
    }
}
