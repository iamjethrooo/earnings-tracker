using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using EarningsTracker.Utilities;

namespace EarningsTracker.Views.Modals;

public partial class LocalInstallationsWindow : Window
{
    private readonly ICollectionView results;
    private readonly LocalInstallationSnapshot snapshot;

    internal LocalInstallationsWindow(LocalInstallationSnapshot snapshot, string? warning = null)
    {
        this.snapshot = snapshot;
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        results = new ListCollectionView(snapshot.Entries.ToList());
        Installations.ItemsSource = results;
        SourceFilter.ItemsSource = new[] { "All sources", LocalGameInstallations.SteamSource,
            LocalGameInstallations.WindowsSource, LocalGameInstallations.FolderSource };
        SourceFilter.SelectedIndex = 0;
        if (snapshot.IncompleteSources.Count > 0)
        {
            var sources = string.Join(", ", snapshot.IncompleteSources);
            var incomplete = $"Some locations could not be checked ({sources}). Results may be incomplete.";
            warning = warning == null ? incomplete : $"{warning} {incomplete}";
        }
        if (warning != null)
        {
            ScanWarning.Text = warning;
            ScanWarning.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => Search.Focus();
    }

    private void FiltersChanged(object sender, RoutedEventArgs e)
    {
        if (results == null) return;
        var search = Search.Text.Trim();
        var source = SourceFilter.SelectedIndex <= 0 ? null : SourceFilter.SelectedItem as string;
        results.Filter = item => item is LocalInstallation installation
            && (source == null || installation.Source == source)
            && installation.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        var count = Installations.Items.Count;
        ResultStatus.Text = !snapshot.IsConfigured ? "Enable a source in Installation settings to see local titles."
            : snapshot.Entries.Count == 0 ? "No titles were found in the locations checked. Reopen this list to retry."
            : count == 0 ? "No titles match this search or source."
            : $"{count} {(count == 1 ? "title" : "titles")}";
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
