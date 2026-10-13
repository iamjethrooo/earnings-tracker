using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using Microsoft.EntityFrameworkCore;
using System.Windows;

namespace nerve_earnings_tracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var shutdownMode = ShutdownMode;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Views.StartupWindow? progress = null;
            try
            {
                progress = new Views.StartupWindow();
                progress.Show();
                await InitializeBeforeOpeningAsync(() =>
                {
                    using var db = new AppDbContext();
                    db.Database.Migrate();
                    DbInitializer.Initialize(db);
                    // Catalog failure must not prevent access to the user's financial records.
                    try { Utilities.CatalogService.ImportBundled(db); }
                    catch (Exception ex) { System.Diagnostics.Trace.TraceError($"Catalog import failed: {ex}"); }
                }, () =>
                {
                    // No financial view is constructed until initialization has succeeded.
                    MainWindow = new MainWindow();
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
        }

        internal static async Task InitializeBeforeOpeningAsync(Action initialize, Action openWindow)
        {
            await Task.Run(initialize);
            openWindow();
        }
    }

}
