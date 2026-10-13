using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Security.Cryptography;
using System.Text;

namespace nerve_earnings_tracker
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
            try
            {
                var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Utilities.AppDataPaths.DirectoryPath.ToUpperInvariant())));
                _instanceMutex = new Mutex(false, @"Local\Nerve.EarningsTracker-" + identity);
                try { _ownsInstanceMutex = _instanceMutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
                if (!_ownsInstanceMutex)
                {
                    MessageBox.Show("The tracker is already running for this data folder. Close the other instance before opening it again.",
                        "Tracker already running", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
                progress = new Views.StartupWindow();
                progress.Show();
                await InitializeBeforeOpeningAsync(() =>
                {
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
                MessageBox.Show($"The tracker could not start. Database initialization or window creation failed.\n\n{ex.GetBaseException().Message}\n\nClose other instances and check database/file permissions before trying again. Your database has not been deleted or recreated.",
                    "Unable to start tracker", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            finally
            {
                if (progress != null) { progress.InitializationFinished = true; progress.Close(); }
                ShutdownMode = shutdownMode;
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
