using System.Windows;
using EarningsTracker.Utilities;

namespace EarningsTracker.Views.Modals;

public partial class CatalogRosterWindow : Window
{
    internal CatalogRosterWindow(CatalogService.RosterPreview preview)
    {
        InitializeComponent();
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40);
        var additions = preview.Additions.Count;
        Summary.Text = $"{additions} {(additions == 1 ? "game" : "games")} ready to add · {preview.Conflicts.Count} existing-title {(preview.Conflicts.Count == 1 ? "conflict" : "conflicts")}";
        Preview.ItemsSource = preview.Additions.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .Select(game => new RosterReviewRow(game.Name, CurrencyDisplay.FormatUsd(game.Terms.RatePerHour), $"{game.Terms.MinutesCap:N0} min", false))
            .Concat(preview.Conflicts.Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => new RosterReviewRow(name, "", "", true)))
            .ToList();
        if (additions == 0)
        {
            Apply.Visibility = Visibility.Collapsed;
            ClosePreview.Content = "Close";
        }
        Loaded += (_, _) => ClosePreview.Focus();
    }

    internal static bool Confirm(FrameworkElement source, CatalogService.RosterPreview preview)
    {
        if (!source.IsLoaded || Window.GetWindow(source) is not { } owner) return false;
        return new CatalogRosterWindow(preview) { Owner = owner }.ShowDialog() == true;
    }

    internal sealed record RosterReviewRow(string Name, string RateText, string CapText, bool IsConflict)
    {
        public string ConflictReason => IsConflict ? "Existing-title conflict · Skipped because it conflicts with an existing game" : "";
        public string Terms => IsConflict ? ConflictReason : $"{RateText}/hr · {CapText} cap";
        public string AccessibleName => $"{Name} · {Terms}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
