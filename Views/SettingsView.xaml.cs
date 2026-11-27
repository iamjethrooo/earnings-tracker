using System.IO;
using System.Text.Json;
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
    private bool canRefreshStoreOffers;
    private CancellationTokenSource? steamWork;
    private readonly SteamLibrary steamLibrary;
    private readonly Func<bool> catalogKeyConfigured;
    private bool settingsReady;
    public SettingsView() : this(null) { }
    internal SettingsView(DbContextOptions<AppDbContext>? databaseOptions, ApplicationUpdates? applicationUpdates = null,
        SteamLibrary? steamLibrary = null, Func<bool>? catalogKeyConfigured = null)
    {
        options = databaseOptions;
        this.steamLibrary = steamLibrary ?? new SteamLibrary(options);
        this.catalogKeyConfigured = catalogKeyConfigured ?? (() => !string.IsNullOrWhiteSpace(CatalogCredentials.Get()));
        InitializeComponent(); model = new(options, applicationUpdates); DataContext = model;
        GamePass.IsChecked = model.GamePass; Automatic.IsChecked = model.AutomaticUpdates;
        GamesDefaultSort.SelectedIndex = model.GamesDefaultSort;
        GamesBookmarksOnly.IsChecked = model.GamesBookmarksOnly;
        GamesShowArchived.IsChecked = model.GamesShowArchived;
        SteamProfile.Text = model.SteamProfile;
        SteamAutomatic.IsChecked = model.SteamAutomatic;
        loadingPreferences = false;
        UpdateKeyStatus();
        UpdateSteamInstallStatus();
        UpdateLocalInstallationSettings();
        settingsReady = true;
        ShowSelectedCategory();
        Unloaded += (_, _) => { ApiKey.Clear(); SteamKey.Clear(); steamWork?.Cancel(); };
    }

    private void SettingsCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (settingsReady && ReferenceEquals(e.OriginalSource, SettingsCategories)) ShowSelectedCategory();
    }

    private void ShowSelectedCategory()
    {
        if (SettingsCategories.SelectedItem is not ListBoxItem { Tag: string sectionName } item) return;
        DataBackupSection.Visibility = sectionName == nameof(DataBackupSection) ? Visibility.Visible : Visibility.Collapsed;
        GamesCategory.Visibility = sectionName == nameof(GamesDisplaySection) ? Visibility.Visible : Visibility.Collapsed;
        AccountsCategory.Visibility = sectionName == nameof(StoreOffersSection) ? Visibility.Visible : Visibility.Collapsed;
        UpdatesCategory.Visibility = sectionName == nameof(ApplicationUpdatesSection) ? Visibility.Visible : Visibility.Collapsed;
        AboutCategory.Visibility = sectionName == nameof(SupportSection) ? Visibility.Visible : Visibility.Collapsed;
        CategoryHeading.Text = System.Windows.Automation.AutomationProperties.GetName(item);
        SettingsScroll.ScrollToTop();
    }

    private void Settings_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!settingsReady) return;
        var compact = e.NewSize.Width < 1050;
        CategoryColumn.Width = new GridLength(compact ? 0 : 260);
        CategoryGap.Width = new GridLength(compact ? 0 : 32);
        Grid.SetRowSpan(CategoryNavigation, compact ? 1 : 2);
        Grid.SetColumnSpan(CategoryNavigation, compact ? 3 : 1);
        Grid.SetColumn(SettingsScroll, compact ? 0 : 2);
        Grid.SetColumnSpan(SettingsScroll, compact ? 3 : 1);
        Grid.SetRow(SettingsScroll, compact ? 1 : 0);
        Grid.SetRowSpan(SettingsScroll, compact ? 1 : 2);
        CategoryNavigation.BorderThickness = compact ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        CategoryNavigation.Padding = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 24, 0);
        CategoryNavigation.Margin = compact ? new Thickness(0, 0, 0, 16) : new Thickness(0);
        SettingsCategories.ItemsPanel = (ItemsPanelTemplate)FindResource(compact ? "SettingsCategoryWrap" : "SettingsCategoryStack");
    }

    private void BackupContents_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        BackupContents.Columns = e.NewSize.Width < 540 ? 1 : 2;
    }

    private void UpdateLocalInstallationSettings()
    {
        try
        {
            var settings = new AppSettings(options);
            WindowsInstallations.IsChecked = settings.Get(LocalGameInstallations.WindowsKey) == "true";
            var count = LocalGameInstallations.ReadFolders(settings.Get(LocalGameInstallations.FoldersKey)).Length;
            LocalFolderStatus.Text = count == 0 ? "No game folders selected." : $"Game folders selected: {count}.";
        }
        catch
        {
            WindowsInstallations.IsEnabled = false;
            LocalFolderStatus.Text = "Installation preferences could not be read. Reopen Settings to retry.";
        }
    }

    private void WindowsInstallations_Click(object sender, RoutedEventArgs e)
    {
        var enabled = WindowsInstallations.IsChecked == true;
        try { new AppSettings(options).Set(LocalGameInstallations.WindowsKey, enabled ? "true" : "false"); }
        catch
        {
            WindowsInstallations.IsChecked = !enabled;
            LocalFolderStatus.Text = "Windows installation preference could not be saved. Try again.";
        }
    }

    private void ChooseLocalFolders_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select individual game installation folders", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var settings = new AppSettings(options);
            var selected = LocalGameInstallations.ReadFolders(settings.Get(LocalGameInstallations.FoldersKey))
                .Concat(dialog.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length > LocalGameInstallations.MaxFolders || selected.Any(path => !LocalGameInstallations.IsLocalFolderPath(path)))
            {
                LocalFolderStatus.Text = $"Choose up to {LocalGameInstallations.MaxFolders} local game folders.";
                return;
            }
            settings.Set(LocalGameInstallations.FoldersKey, JsonSerializer.Serialize(selected));
            UpdateLocalInstallationSettings();
        }
        catch { LocalFolderStatus.Text = "Game folders could not be saved. Try again."; }
    }

    private void ClearLocalFolders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            new AppSettings(options).Set(LocalGameInstallations.FoldersKey, "");
            UpdateLocalInstallationSettings();
        }
        catch { LocalFolderStatus.Text = "Game folders could not be cleared. Try again."; }
    }
    private void UpdateSteamInstallStatus()
    {
        try
        {
            var folders = new AppSettings(options).Get(SteamInstallations.SettingsKey);
            SteamInstallStatus.Text = folders.Length == 0 ? "Installation indicators are off." : "Installation indicators are on.";
        }
        catch
        {
            SteamInstallStatus.Text = "Installation preference could not be read.";
        }
    }

    private void ChooseSteamFolders_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose local Steam steamapps folders", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var folders = dialog.FolderNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (folders.Length > SteamInstallations.MaxFolders || folders.Any(folder => !SteamInstallations.IsLibraryFolder(folder)))
        {
            SteamInstallStatus.Text = $"Choose up to {SteamInstallations.MaxFolders} local folders named steamapps.";
            return;
        }
        SaveSteamFolders(JsonSerializer.Serialize(folders));
    }

    private void DisableSteamFolders_Click(object sender, RoutedEventArgs e) => SaveSteamFolders("");

    private void SaveSteamFolders(string folders)
    {
        try
        {
            new AppSettings(options).Set(SteamInstallations.SettingsKey, folders);
            UpdateSteamInstallStatus();
        }
        catch
        {
            SteamInstallStatus.Text = "Installation preference could not be saved. Try again.";
        }
    }
    private async void SteamLink_Click(object sender, RoutedEventArgs e)
    {
        var profile = SteamProfile.Text;
        var key = SteamKey.Password;
        SteamKey.Clear();
        await RunSteamAsync(async token =>
        {
            await steamLibrary.LinkAsync(profile, key, token);
            return "Steam linked and synced. Matching games appear under Steam library.";
        });
    }
    private async void SteamSync_Click(object sender, RoutedEventArgs e)
        => await RunSteamAsync(token => steamLibrary.RefreshAsync(true, token));
    private async void SteamUnlink_Click(object sender, RoutedEventArgs e)
        => await RunSteamAsync(async token =>
        {
            await steamLibrary.UnlinkAsync(token);
            return "Steam unlinked. Manual ownership and purchase costs are kept.";
        });
    private async Task RunSteamAsync(Func<CancellationToken, Task<string>> action)
    {
        if (steamWork != null) return;
        using var cancellation = new CancellationTokenSource();
        steamWork = cancellation;
        SteamControls.IsEnabled = false;
        SteamFeedback.Text = "Working with Steam…";
        try
        {
            var message = await action(cancellation.Token);
            model.Reload();
            SteamProfile.Text = model.SteamProfile;
            SteamFeedback.Text = message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { SteamFeedback.Text = "Steam operation cancelled. Reopen Settings to see the saved link."; }
        catch (ArgumentException ex) { SteamFeedback.Text = ex.Message; }
        catch (SteamRateLimitException ex) { SteamFeedback.Text = ex.Message; }
        catch (Exception ex)
        {
            // Never log HTTP exception messages: they can contain the Steam key.
            System.Diagnostics.Trace.TraceWarning($"Steam operation failed ({ex.GetType().Name}).");
            SteamFeedback.Text = "Steam could not complete the request. Check your profile, API key and Game details privacy, then retry. Saved library evidence is kept.";
        }
        finally { steamWork = null; SteamControls.IsEnabled = true; SteamKey.Clear(); }
    }
    private void SteamAutomatic_Click(object sender, RoutedEventArgs e)
    {
        try { model.SetSteamAutomatic(SteamAutomatic.IsChecked == true); SteamFeedback.Text = "Steam sync preference saved."; }
        catch { SteamAutomatic.IsChecked = SteamAutomatic.IsChecked != true; SteamFeedback.Text = "Steam sync preference could not be saved. Retry."; }
    }
    private void SteamKeyHelp_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://steamcommunity.com/dev/apikey") { UseShellExecute = true }); }
        catch { SteamFeedback.Text = "Open https://steamcommunity.com/dev/apikey in your browser to obtain your personal key."; }
    }
    private void UpdateKeyStatus()
    {
        try
        {
            canRefreshStoreOffers = catalogKeyConfigured();
            KeyStatus.Text = canRefreshStoreOffers ? "Saved" : "Not configured";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            canRefreshStoreOffers = false;
            KeyStatus.Text = "Credential status unavailable";
        }
        RefreshOffers.Content = canRefreshStoreOffers ? "Refresh store offers" : "Set up price refresh…";
    }
    private void ReleaseHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EarningsTracker.Views.Modals.ReleaseHistoryWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
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
        Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = false;
        ShowFeedback("Checking for game list updates…", target: CatalogUpdateFeedback);
        string? result = null;
        try
        {
            result = await new CatalogUpdates(options).RefreshAsync(true,
                confirmRoster: preview => Modals.CatalogRosterWindow.Confirm(this, preview));
            var tone = result.Contains("keeping saved", StringComparison.OrdinalIgnoreCase)
                ? FeedbackTone.Warning : FeedbackTone.Neutral;
            ShowFeedback(result, tone, CatalogUpdateFeedback);
            model.Reload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Game list update check failed ({ex.GetType().Name}).");
            ShowFeedback(result == null ? "Could not complete the update check. Reopen Settings or retry later."
                : result + " Reopen Settings to reload update details.", FeedbackTone.Warning, CatalogUpdateFeedback);
        }
        finally { Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = true; }
    }
    private async void RefreshOffers_Click(object sender, RoutedEventArgs e)
    {
        if (!canRefreshStoreOffers)
        {
            PriceRefreshSetup.IsExpanded = true;
            ApiKey.BringIntoView();
            ApiKey.Focus();
            return;
        }

        Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = false;
        ShowFeedback("Refreshing store offers…", target: StoreOffersFeedback);
        try
        {
            var result = await new CatalogUpdates(options).RefreshStoreOffersAsync();
            var tone = result.StartsWith("Store offers unavailable", StringComparison.Ordinal)
                ? FeedbackTone.Warning : FeedbackTone.Neutral;
            ShowFeedback(result, tone, StoreOffersFeedback);
            model.Reload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Store offer refresh failed ({ex.GetType().Name}).");
            ShowFeedback("Could not complete the offer refresh. Reopen Settings or retry later.", FeedbackTone.Error, StoreOffersFeedback);
        }
        finally
        {
            UpdateKeyStatus();
            Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = true;
        }
    }
    private async void RollbackCatalog_Click(object sender, RoutedEventArgs e)
    {
        Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = false;
        bool committed = false;
        try
        {
            var versions = await Task.Run(() =>
            {
                using var db = options == null ? new AppDbContext() : new AppDbContext(options);
                return CatalogService.GetRollbackVersions(db);
            });
            var confirmation = MessageBox.Show(Window.GetWindow(this),
                $"Restore catalog {versions.Target} in place of {versions.Active}? This restores catalog metadata and mappings. Your financial records, manual ownership and settings are preserved.",
                "Confirm catalog restore", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) return;

            await Task.Run(() =>
            {
                using var db = options == null ? new AppDbContext() : new AppDbContext(options);
                CatalogService.Rollback(db, versions);
            });
            committed = true;
            model.Reload();
            ShowFeedback($"Catalog {versions.Target} restored.", FeedbackTone.Success);
        }
        catch (InvalidDataException ex) when (!committed)
        {
            ShowFeedback(ex.Message, FeedbackTone.Warning);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            ShowFeedback(committed
                ? "Catalog restored, but Settings could not refresh. Reopen Settings."
                : "Catalog restore could not be completed. Check the previous version and retry.", FeedbackTone.Warning);
        }
        finally { Refresh.IsEnabled = RefreshOffers.IsEnabled = RollbackCatalog.IsEnabled = true; }
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
    internal void ShowFeedback(string message, FeedbackTone tone = FeedbackTone.Neutral, TextBlock? target = null)
    {
        var resource = tone switch
        {
            FeedbackTone.Success => "VerifiedTextColor",
            FeedbackTone.Warning => "PendingTextColor",
            FeedbackTone.Error => "RejectedTextColor",
            _ => "SecondaryTextColor"
        };
        target ??= Feedback;
        target.SetResourceReference(TextBlock.ForegroundProperty, resource);
        target.Text = message;
    }
}

internal enum FeedbackTone { Neutral, Success, Warning, Error }
