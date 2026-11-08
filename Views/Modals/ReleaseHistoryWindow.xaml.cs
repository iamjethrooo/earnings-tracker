using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using EarningsTracker.Utilities;

namespace EarningsTracker.Views.Modals;

public partial class ReleaseHistoryWindow : Window
{
    public ReleaseHistoryWindow() : this(ReleaseHistory.InstalledVersion, ReleaseHistory.Load) { }

    internal ReleaseHistoryWindow(string? installedVersion, Func<string?, IReadOnlyList<ReleaseHistoryEntry>> load)
    {
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        try
        {
            var entries = load(installedVersion);
            Releases.ItemsSource = entries;
            var installed = entries.FirstOrDefault(entry => entry.IsInstalled);
            HistoryStatus.Text = entries.Count == 0 ? "No release notes are included in this build."
                : string.IsNullOrWhiteSpace(installedVersion) ? "Development build · All included versions are available offline."
                : installed == null ? $"Version {installedVersion} is installed; its notes are not included in this build."
                : $"Version {installedVersion} is installed · All included versions are available offline.";
            Loaded += (_, _) =>
            {
                var initial = installed ?? entries.FirstOrDefault();
                if (initial == null) return;
                Releases.SelectedItem = initial;
                Releases.ScrollIntoView(initial);
                Releases.UpdateLayout();
                if (Releases.ItemContainerGenerator.ContainerFromItem(initial) is ListBoxItem item)
                    item.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            };
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Release history could not be loaded: {ex}");
            HistoryStatus.Text = "Release history could not be loaded. Close this dialog and try again. Application updates remain available in Settings.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
