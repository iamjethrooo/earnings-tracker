using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace nerve_earnings_tracker.Utilities;

internal enum ApplicationUpdateState { Unavailable, Idle, Checking, UpToDate, Available, Downloading, ReadyToRestart, Error }

// Owned by App, shared with pages. SDK assets stay here rather than entering the view layer.
internal sealed class ApplicationUpdates : INotifyPropertyChanged, IDisposable
{
    internal const string LocalSourceVariable = "NERVE_TRACKER_LOCAL_UPDATE_SOURCE";
    private readonly UpdateManager manager;
    private readonly Dispatcher dispatcher;
    private readonly UpdateRestartGuard guard;
    private readonly Func<bool> editorOpen;
    private readonly Func<Action> prepareExit;
    private readonly Action shutdown;
    private readonly Action<VelopackAsset> armUpdates;
    private UpdateInfo? available;
    private VelopackAsset? pending;
    private CancellationTokenSource? cancellation;
    private bool disposed;
    private bool startupChecked;
    private int operationId;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ApplicationUpdateState State { get; private set; }
    public string Message { get; private set; } = "Check for a new application version.";
    public int Progress { get; private set; }
    public string Version => pending?.Version.ToString() ?? available?.TargetFullRelease.Version.ToString() ?? "";
    public string CurrentVersion { get; private set; } = "Development build";
    public bool CanCheck => !disposed && State is ApplicationUpdateState.Idle or ApplicationUpdateState.UpToDate or ApplicationUpdateState.Available or ApplicationUpdateState.Error;
    public bool CanDownload => !disposed && available != null && State is ApplicationUpdateState.Available or ApplicationUpdateState.Error;
    public bool CanCancel => !disposed && State is ApplicationUpdateState.Checking or ApplicationUpdateState.Downloading;
    public bool CanRestart => !disposed && State == ApplicationUpdateState.ReadyToRestart && !guard.IsBusy && !editorOpen();
    public bool IsDownloading => State == ApplicationUpdateState.Downloading;
    public bool HasNotice => State is ApplicationUpdateState.Available or ApplicationUpdateState.ReadyToRestart
        || State == ApplicationUpdateState.Error && available != null;
    public string Notice => State == ApplicationUpdateState.ReadyToRestart ? $"Version {Version} is ready. Restart when convenient." : $"Version {Version} is available.";
    public string RestartHint => CanRestart ? "Restart is always your choice." : "Close editing/import dialogs and wait for database work before restarting.";

    internal static UpdateManager CreateManager()
    {
        var local = Environment.GetEnvironmentVariable(LocalSourceVariable);
        // Local feeds are permitted only alongside the explicit isolated data override.
        if (!string.IsNullOrEmpty(local))
        {
            if (!AppDataPaths.HasOverride || !Path.IsPathFullyQualified(local))
                Trace.TraceWarning("Ignoring local update source: an absolute feed path and an isolated data directory are required.");
            else return new UpdateManager(Path.GetFullPath(local));
        }
        return new UpdateManager(new GithubSource("https://github.com/iamjethrooo/nerve-earnings-tracker", null, false));
    }

    internal ApplicationUpdates(UpdateManager manager, Dispatcher dispatcher, UpdateRestartGuard guard,
        Func<bool> editorOpen, Func<Action> prepareExit, Action shutdown, Action<VelopackAsset>? armUpdates = null)
    {
        this.manager = manager; this.dispatcher = dispatcher; this.guard = guard;
        this.editorOpen = editorOpen; this.prepareExit = prepareExit; this.shutdown = shutdown;
        this.armUpdates = armUpdates ?? (asset => manager.WaitExitThenApplyUpdates(asset, silent: true, restart: true));
        try
        {
            CurrentVersion = manager.CurrentVersion?.ToString() ?? "Development build";
            State = manager.IsInstalled ? ApplicationUpdateState.Idle : ApplicationUpdateState.Unavailable;
            if (!manager.IsInstalled) Message = "Application updates are available in installed builds. This development build works normally.";
            else if ((pending = manager.UpdatePendingRestart) != null) Set(ApplicationUpdateState.ReadyToRestart, "A downloaded update is ready. Restart when convenient.");
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Application updater initialization failed: {ex}");
            State = ApplicationUpdateState.Unavailable;
            Message = "Application updates are unavailable in this launch. Your saved data remains usable. Try again on a later launch.";
        }
        guard.Changed += GuardChanged;
    }

    private void GuardChanged() => Publish(Notify);
    internal void RefreshRestartEligibility() => Notify();
    private void Publish(Action action)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else
        {
            try { dispatcher.BeginInvoke(() => { if (!disposed) action(); }); }
            catch (InvalidOperationException) when (dispatcher.HasShutdownStarted) { }
        }
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private void Set(ApplicationUpdateState state, string message) { State = state; Message = message; Notify(); }

    internal async Task CheckOnStartupAsync(bool enabled)
    {
        if (startupChecked || disposed) return;
        startupChecked = true;
        if (enabled && CanCheck) await CheckAsync();
    }

    internal async Task CheckAsync()
    {
        dispatcher.VerifyAccess();
        if (!CanCheck) return;
        var previous = available;
        var id = ++operationId;
        cancellation = new();
        var token = cancellation.Token;
        Set(ApplicationUpdateState.Checking, "Checking for application updates…");
        try
        {
            // The pinned SDK has no check token. Keep the operation serialized until it finishes,
            // then discard a cancelled result; downloads have real SDK cancellation.
            var result = await Task.Run(manager.CheckForUpdatesAsync);
            if (disposed || id != operationId) return;
            token.ThrowIfCancellationRequested();
            available = result;
            Set(result == null ? ApplicationUpdateState.UpToDate : ApplicationUpdateState.Available,
                result == null ? "You have the latest application version." : $"Version {Version} is available. Download when convenient.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { if (!disposed) { available = previous; Set(previous == null ? ApplicationUpdateState.Idle : ApplicationUpdateState.Available, "Check cancelled. You can retry."); } }
        catch (Exception ex)
        { if (!disposed) { Trace.TraceError($"Application update check failed: {ex}"); available = previous; Set(ApplicationUpdateState.Error, "Unable to check for updates. Saved data remains available. Check your connection and retry."); } }
        finally { cancellation?.Dispose(); cancellation = null; }
    }

    internal async Task DownloadAsync()
    {
        dispatcher.VerifyAccess();
        if (!CanDownload) return;
        var update = available!;
        var id = ++operationId;
        cancellation = new();
        var token = cancellation.Token;
        Progress = 0;
        Set(ApplicationUpdateState.Downloading, "Downloading application update…");
        try
        {
            await Task.Run(() => manager.DownloadUpdatesAsync(update, value => Publish(() =>
            {
                if (id == operationId && State == ApplicationUpdateState.Downloading)
                { Progress = Math.Clamp(value, 0, 100); Notify(); }
            }), token));
            if (disposed) return;
            pending = manager.UpdatePendingRestart ?? update.TargetFullRelease;
            Progress = 100;
            Set(ApplicationUpdateState.ReadyToRestart, "Download complete. Restart when convenient to apply the update.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!disposed)
            {
                pending = manager.UpdatePendingRestart;
                Set(pending != null ? ApplicationUpdateState.ReadyToRestart : ApplicationUpdateState.Available,
                    pending != null ? "The completed download is ready for restart." : "Download cancelled. You can retry.");
            }
        }
        catch (Exception ex)
        { if (!disposed) { Trace.TraceError($"Application update download failed: {ex}"); Set(ApplicationUpdateState.Error, "Download failed. Your installed version and saved data are unchanged. Retry when connected."); } }
        finally { cancellation?.Dispose(); cancellation = null; }
    }

    internal void Cancel() { cancellation?.Cancel(); if (CanCancel) { Message = "Cancelling…"; Notify(); } }

    internal bool Restart()
    {
        dispatcher.VerifyAccess();
        if (!CanRestart || pending == null) { Notify(); return false; }
        using var preparation = guard.TryPrepare();
        if (preparation == null || editorOpen()) { Notify(); return false; }
        Action? restore = null;
        try
        {
            restore = prepareExit(); // Detach pages and flush before the SDK's bounded exit wait.
            armUpdates(pending);
            shutdown();
            return true;
        }
        catch (Exception ex)
        { preparation.Dispose(); restore?.Invoke(); Trace.TraceError($"Application update restart failed: {ex}"); Message = "Restart could not be prepared. Your update is still ready; retry after closing other work."; Notify(); return false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; operationId++; cancellation?.Cancel();
        guard.Changed -= GuardChanged;
    }
}
