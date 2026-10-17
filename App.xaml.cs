using EarningsTracker.Data;
using EarningsTracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Security.Cryptography;
using System.Text;

namespace EarningsTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Mutex? _instanceMutex;
        private bool _ownsInstanceMutex;
        private Utilities.ApplicationUpdates? _applicationUpdates;
        [STAThread]
        private static void Main(string[] args)
        {
            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var shutdownMode = ShutdownMode;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Views.StartupWindow? progress = null;
            int? startupExitCode = null;
            try
            {
                Utilities.ApplicationUpdateConfiguration.EnsureTestDataIsolation();
                var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Utilities.AppDataPaths.InstanceDirectoryPath.ToUpperInvariant())));
                _instanceMutex = new Mutex(false, @"Local\Nerve.EarningsTracker-" + identity);
                try { _ownsInstanceMutex = _instanceMutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
                if (!_ownsInstanceMutex)
                {
                    MessageBox.Show("The tracker is already running for this data folder. Close the other instance before opening it again.",
                        "Tracker already running", MessageBoxButton.OK, MessageBoxImage.Information);
                    startupExitCode = 0;
                    return;
                }
                progress = new Views.StartupWindow();
                progress.Show();
                var copiedUserData = false;
                string? restoredRecovery = null;
                await InitializeBeforeOpeningAsync(() =>
                {
                    copiedUserData = Utilities.AppDataPaths.EnsureDataLocation();
                    restoredRecovery = Utilities.ApplicationBackup.ApplyPendingRestore(Utilities.AppDataPaths.DirectoryPath);
                    using var db = new AppDbContext();
                    Utilities.DatabaseInitialization.Initialize(db);
                    // Catalog failure must not prevent access to the user's financial records.
                    try { Utilities.CatalogService.ImportBundled(db); }
                    catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Catalog import failed: {ex}"); }
                }, () =>
                {
                    // No financial view is constructed until initialization has succeeded.
                    var window = new MainWindow();
                    MainWindow = window;
                    try
                    {
                        _applicationUpdates = new Utilities.ApplicationUpdates(Utilities.ApplicationUpdates.CreateManager(), Dispatcher,
                            Utilities.UpdateRestartGuard.Shared, () => window.IsEditorOpen, window.PrepareUpdateExit, Shutdown);
                        window.AttachApplicationUpdates(_applicationUpdates);
                    }
                    catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Application updater is unavailable: {ex}"); }
                    MainWindow.Show();
                });
                if (copiedUserData)
                    MessageBox.Show(MainWindow, $"Your saved data and backups were copied and verified in:\n{Utilities.AppDataPaths.DefaultDirectoryPath}\n\nThe former files were retained. Use this application from now on; changes made in the former application will not appear here. A recovery snapshot is in Backups\\BeforeInstallSeparation.",
                        "Saved data preserved", MessageBoxButton.OK, MessageBoxImage.Information);
                if (restoredRecovery != null)
                    MessageBox.Show(MainWindow, $"The selected backup was restored and verified. The previous database was preserved at:\n{restoredRecovery}",
                        "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                MessageBox.Show($"The tracker could not start. Database initialization, pending restore, or window creation failed.\n\n{ex.GetBaseException().Message}\n\nClose other instances and check database/file permissions before trying again. Preserve the data folder and its Backups for recovery. If you are upgrading, keep the former installation and data intact; do not uninstall or repair it until the new app starts successfully.",
                    "Unable to start tracker", MessageBoxButton.OK, MessageBoxImage.Error);
                startupExitCode = 1;
            }
            finally
            {
                if (progress != null) { progress.InitializationFinished = true; progress.Close(); }
                ShutdownMode = shutdownMode;
                // WPF rejects ShutdownMode changes once Shutdown has begun. Finish startup
                // cleanup first so a handled initialization failure exits without a second crash.
                if (startupExitCode.HasValue) Shutdown(startupExitCode.Value);
            }
            if (_applicationUpdates != null && MainWindow?.IsVisible == true)
            {
                try { await _applicationUpdates.CheckOnStartupAsync(new Utilities.AppSettings().Get("applicationUpdates.startup") != "false"); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Startup update preference could not be read: {ex}"); }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _applicationUpdates?.Dispose();
            if (_ownsInstanceMutex) { _instanceMutex?.ReleaseMutex(); _ownsInstanceMutex = false; }
            _instanceMutex?.Dispose();
            base.OnExit(e);
        }

        internal static async Task InitializeBeforeOpeningAsync(Action initialize, Action openWindow)
        {
            await Task.Run(initialize);
            openWindow();
        }
    }

}
