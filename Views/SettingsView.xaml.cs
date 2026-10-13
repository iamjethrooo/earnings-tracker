using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;

namespace nerve_earnings_tracker.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel model;
    private readonly DbContextOptions<AppDbContext>? options;
    public SettingsView() : this(null) { }
    internal SettingsView(DbContextOptions<AppDbContext>? databaseOptions)
    {
        options = databaseOptions;
        InitializeComponent(); model = new(options); DataContext = model;
        GamePass.IsChecked = model.GamePass; Automatic.IsChecked = model.AutomaticUpdates;
        UpdateKeyStatus(); Unloaded += (_, _) => ApiKey.Clear();
    }
    private void UpdateKeyStatus()
    {
        try { KeyStatus.Text = string.IsNullOrEmpty(CatalogCredentials.Get()) ? "Not configured" : "Saved"; }
        catch { KeyStatus.Text = "Credential status unavailable"; }
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
        var dialog = new SaveFileDialog { FileName = "nerve-diagnostics.json", Filter = "JSON report (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, model.Diagnostics()); ShowFeedback("Diagnostics exported.", FeedbackTone.Success); }
        catch { ShowFeedback("Diagnostics could not be exported. Choose another location or retry.", FeedbackTone.Error); }
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
