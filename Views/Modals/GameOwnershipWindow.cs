using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using nerve_earnings_tracker.ViewModels;

namespace nerve_earnings_tracker.Views.Modals;

internal sealed class GameOwnershipWindow : Window
{
    internal GameOwnershipWindow(GameItemViewModel item)
    {
        Title = "Ownership / purchase cost"; Width = 450; SizeToContent = SizeToContent.Height;
        MinWidth = 360; MinHeight = 300;
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (TryFindResource("AppFont") is FontFamily appFont) FontFamily = appFont;
        Background = (Brush)FindResource("DefaultBackgroundColor");
        Foreground = (Brush)FindResource("DefaultTextColor");
        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(new TextBlock { Text = item.Game.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var owned = new CheckBox { Content = "I own this game", IsChecked = item.IsOwned, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(owned);
        panel.Children.Add(new TextBlock { Text = "Owned on / acquisition source" });
        var source = new ComboBox { IsEditable = true, Text = item.Ownership?.Source ?? "", Margin = new Thickness(0, 5, 0, 12),
            ItemsSource = new[] { "Steam", "Epic Games", "Xbox / Microsoft Store", "GOG", "EA app", "Ubisoft Connect", "Battle.net", "Direct download" } };
        panel.Children.Add(source);
        panel.Children.Add(new TextBlock { Text = "Purchase cost (optional; 0 for a free acquisition)" });
        var cost = new TextBox { Text = item.Ownership?.PurchaseAmount?.ToString(CultureInfo.CurrentCulture) ?? "", Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(cost);
        panel.Children.Add(new TextBlock { Text = "Currency (three-letter code)" });
        var currency = new TextBox { Text = item.Ownership?.Currency ?? "PHP", MaxLength = 3, Margin = new Thickness(0, 5, 0, 12) };
        panel.Children.Add(currency);
        cost.Style = currency.Style = (Style)FindResource("ModalTextBox");
        AutomationProperties.SetName(source, "Owned on / acquisition source");
        AutomationProperties.SetName(cost, "Purchase cost (optional)");
        AutomationProperties.SetName(currency, "Currency (three-letter code)");
        panel.Children.Add(new TextBlock { Text = "Subscription access is managed in Settings. Purchase estimates use current exchange rates and do not change earnings records.",
            Style = (Style)FindResource("DashboardStatDescription"), TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { Style = (Style)FindResource("InlineValidationText"), Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Polite);
        panel.Children.Add(error);
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Margin = new Thickness(0, 12, 0, 0) };
        save.Style = (Style)FindResource("PrimaryButton");
        panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            decimal? amount = null;
            if (!string.IsNullOrWhiteSpace(cost.Text))
            {
                if (!decimal.TryParse(cost.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || parsed < 0)
                { error.Text = "Enter a nonnegative purchase amount."; cost.Focus(); cost.SelectAll(); cost.BringIntoView(); return; }
                amount = parsed;
            }
            try { item.SaveOwnership(owned.IsChecked == true, source.Text.Trim(), amount, currency.Text.Trim().ToUpperInvariant()); DialogResult = true; }
            catch (ArgumentException ex) { error.Text = ex.Message; currency.Focus(); currency.SelectAll(); currency.BringIntoView(); }
            catch (Exception) { error.Text = "Ownership could not be saved. Please retry."; }
        };
    }
}
