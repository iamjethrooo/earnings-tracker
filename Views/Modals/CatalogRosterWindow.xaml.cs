using System.Windows;
using EarningsTracker.Utilities;

namespace EarningsTracker.Views.Modals;

public partial class CatalogRosterWindow : Window
{
    internal CatalogRosterWindow(CatalogService.RosterPreview preview)
    {
        InitializeComponent();
        var additions = preview.Additions.Count;
        Summary.Text = $"Games ready to add: {additions}. Existing title conflicts: {preview.Conflicts.Count}.";
        Preview.ItemsSource = preview.Additions.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .Select(game => $"{game.Name} · {CurrencyDisplay.FormatUsd(game.Terms.RatePerHour)}/hr · {game.Terms.MinutesCap:N0} min cap")
            .Concat(preview.Conflicts.Order(StringComparer.OrdinalIgnoreCase)
                .Select(name => $"{name} · Skipped because it conflicts with an existing game"))
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

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
