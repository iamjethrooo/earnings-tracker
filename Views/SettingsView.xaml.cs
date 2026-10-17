using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;

namespace EarningsTracker.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel model;
    private readonly DbContextOptions<AppDbContext>? options;
    private bool loadingPreferences = true;
    public SettingsView() : this(null) { }
    internal SettingsView(DbContextOptions<AppDbContext>? databaseOptions, ApplicationUpdates? applicationUpdates = null)
    {
        options = databaseOptions;
        InitializeComponent(); model = new(options, applicationUpdates); DataContext = model;
        GamePass.IsChecked = model.GamePass; Automatic.IsChecked = model.AutomaticUpdates;
        GamesDefaultSort.SelectedIndex = model.GamesDefaultSort;
        GamesBookmarksOnly.IsChecked = model.GamesBookmarksOnly;
        GamesShowArchived.IsChecked = model.GamesShowArchived;
        loadingPreferences = false;
        UpdateKeyStatus(); Unloaded += (_, _) => ApiKey.Clear();
    }
    private void UpdateKeyStatus()
    {
        try { KeyStatus.Text = string.IsNullOrEmpty(CatalogCredentials.Get()) ? "Not configured" : "Saved"; }
        catch { KeyStatus.Text = "Credential status unavailable"; }
    }
    private async void CheckApplication_Click(object sender, RoutedEventArgs e)
    { if (model.ApplicationUpdates != null) await model.ApplicationUpdates.CheckAsync(); }
    private async void DownloadApplication_Click(object sender, RoutedEventArgs e)
    { if (model.ApplicationUpdates != null) await model.ApplicationUpdates.DownloadAsync(); }
    private void CancelApplication_Click(object sender, RoutedEventArgs e) => model.ApplicationUpdates?.Cancel();
    private void RestartApplication_Click(object sender, RoutedEventArgs e) => model.ApplicationUpdates?.Restart();
    private void ApplicationStartup_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetApplicationStartup(ApplicationStartup.IsChecked == true); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); ApplicationStartup.IsChecked = ApplicationStartup.IsChecked != true; ShowFeedback("Application update preference could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void Subscription_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetGamePass(GamePass.IsChecked == true); ShowFeedback("Subscription saved.", FeedbackTone.Success); }
        catch { GamePass.IsChecked = GamePass.IsChecked != true; ShowFeedback("Subscription could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void Automatic_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetAutomaticUpdates(Automatic.IsChecked == true); ShowFeedback("Update preference saved.", FeedbackTone.Success); }
        catch { Automatic.IsChecked = Automatic.IsChecked != true; ShowFeedback("Update preference could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void GamesDefaultSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingPreferences || GamesDefaultSort.SelectedIndex < 0) return;
        try { model.SetGamesDefaultSort(GamesDefaultSort.SelectedIndex); ShowFeedback("Games default sort saved.", FeedbackTone.Success); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); loadingPreferences = true; GamesDefaultSort.SelectedIndex = model.GamesDefaultSort; loadingPreferences = false; ShowFeedback("Games default sort could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void GamesBookmarksOnly_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetGamesBookmarksOnly(GamesBookmarksOnly.IsChecked == true); ShowFeedback("Games bookmark default saved.", FeedbackTone.Success); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); GamesBookmarksOnly.IsChecked = model.GamesBookmarksOnly; ShowFeedback("Games bookmark default could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void GamesShowArchived_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetGamesShowArchived(GamesShowArchived.IsChecked == true); ShowFeedback("Archived games display saved.", FeedbackTone.Success); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); GamesShowArchived.IsChecked = model.GamesShowArchived; ShowFeedback("Archived games display could not be saved. Retry.", FeedbackTone.Error); }
    }
    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKey.Password)) { ShowFeedback("Enter a replacement key. Your saved key is unchanged.", FeedbackTone.Warning); return; }
        try { CatalogCredentials.Save(ApiKey.Password.Trim()); ShowFeedback("API key saved.", FeedbackTone.Success); }
        catch { ShowFeedback("API key could not be saved. Retry.", FeedbackTone.Error); }
        finally { ApiKey.Clear(); UpdateKeyStatus(); }
    }
    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        try { CatalogCredentials.Remove(); ShowFeedback("Saved API key removed.", FeedbackTone.Success); }
        catch { ShowFeedback("API key could not be removed. Retry.", FeedbackTone.Error); }
        finally { ApiKey.Clear(); UpdateKeyStatus(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Refresh.IsEnabled = false; ShowFeedback("Checking catalog, exchange rates, and live prices…");
        try
        {
            var result = await new CatalogUpdates(options).RefreshAsync(true);
            model.Reload();
            // Update results can contain partial failures; keep those warnings visible.
            ShowFeedback(result, result.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? FeedbackTone.Warning : FeedbackTone.Neutral);
        }
        catch { ShowFeedback("Update failed. Saved data remains available. Retry later.", FeedbackTone.Error); }
        finally { Refresh.IsEnabled = true; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "EarningsTracker-diagnostics.json", Filter = "JSON report (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, model.Diagnostics()); ShowFeedback("Diagnostics exported.", FeedbackTone.Success); }
        catch { ShowFeedback("Diagnostics could not be exported. Choose another location or retry.", FeedbackTone.Error); }
    }
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(AppDataPaths.DirectoryPath, "Backups");
        var dialog = new SaveFileDialog
        {
            FileName = $"EarningsTracker-{DateTime.Now:yyyyMMdd-HHmmss}.db",
            Filter = "Earnings Tracker backup (*.db)|*.db",
            InitialDirectory = directory
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        BackupButton.IsEnabled = RestoreButton.IsEnabled = false;
        ShowFeedback("Creating and verifying backup…");
        try
        {
            var path = await Task.Run(() => ApplicationBackup.Create(AppDataPaths.DirectoryPath, dialog.FileName));
            ShowFeedback($"Backup verified and saved: {path}", FeedbackTone.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            ShowFeedback($"Backup failed. Your database is unchanged. {ex.GetBaseException().Message}", FeedbackTone.Error);
        }
        finally { BackupButton.IsEnabled = RestoreButton.IsEnabled = true; }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Earnings Tracker backup (*.db)|*.db", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        BackupButton.IsEnabled = RestoreButton.IsEnabled = false;
        ShowFeedback("Validating selected backup…");
        try
        {
            await Task.Run(() => ApplicationBackup.Validate(dialog.FileName));
            var confirmation = MessageBox.Show(Window.GetWindow(this),
                "Restore this backup? On the next launch it will replace all current sessions, payouts, audit history, and saved settings with the selected backup. The current database will first be copied to a verified recovery file in Backups\\BeforeUserRestore. Cached artwork and your saved API key are unaffected. The application will close; reopen it to finish the restore.",
                "Confirm data replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) { ShowFeedback("Restore cancelled. Current data is unchanged."); return; }
            await Task.Run(() => ApplicationBackup.ScheduleRestore(AppDataPaths.DirectoryPath, dialog.FileName));
            MessageBox.Show(Window.GetWindow(this), "The backup is validated and ready. Reopen Earnings Tracker to complete the restore. If recovery protection or replacement fails, startup will stop and retain the current data and recovery files.",
                "Restore ready", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            ShowFeedback($"Restore could not be prepared. Current data is unchanged. {ex.GetBaseException().Message}", FeedbackTone.Error);
        }
        finally { BackupButton.IsEnabled = RestoreButton.IsEnabled = true; }
    }
    internal void ShowFeedback(string message, FeedbackTone tone = FeedbackTone.Neutral)
    {
        var resource = tone switch
        {
            FeedbackTone.Success => "VerifiedTextColor",
            FeedbackTone.Warning => "PendingTextColor",
            FeedbackTone.Error => "RejectedTextColor",
            _ => "SecondaryTextColor"
        };
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, resource);
        Feedback.Text = message;
    }
}

internal enum FeedbackTone { Neutral, Success, Warning, Error }
