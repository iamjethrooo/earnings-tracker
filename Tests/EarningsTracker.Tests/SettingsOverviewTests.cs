using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Utilities;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class SettingsOverviewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 720, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task CategoriesKeepActionsPreferencesDraftsAndFooterReachable(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var settings = new SettingsView(database.Options, catalogKeyConfigured: static () => false);
        var window = new MainWindow(page => page == NavigationPage.Settings ? settings : new UserControl())
        {
            Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false
        };
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        async Task Capture(string name)
        {
            await Settle();
            Save((FrameworkElement)window.Content, $"{name}-{width}x{height}-{scale:0.0}", scale);
        }
        var apiKey = (PasswordBox)settings.FindName("ApiKey");
        var steamKey = (PasswordBox)settings.FindName("SteamKey");
        try
        {
            window.Show();
            window.NavigateTo(NavigationPage.Settings);
            await Settle();
            var categories = (ListBox)settings.FindName("SettingsCategories");
            Assert.Equal(5, categories.Items.Count);
            Assert.Equal("DataBackupSection", ((ListBoxItem)categories.SelectedItem).Tag);
            Assert.Equal("Data & backup", ((TextBlock)settings.FindName("CategoryHeading")).Text);
            Assert.True(((Border)settings.FindName("DataBackupSection")).IsVisible);
            Assert.False(((FrameworkElement)settings.FindName("GamesCategory")).IsVisible);
            Assert.False(((FrameworkElement)settings.FindName("AccountsCategory")).IsVisible);
            Assert.Contains("replaces saved data", ((TextBlock)settings.FindName("RestoreConsequencesText")).Text);
            var scroll = (ScrollViewer)settings.FindName("SettingsScroll");
            Assert.Equal(0, scroll.ScrollableWidth);
            await Capture("data-backup");
            // Inspect both file-picker entry points without opening either picker or performing a restore.
            foreach (var name in new[] { "BackupButton", "RestoreButton" })
            {
                var action = (Button)settings.FindName(name);
                action.BringIntoView();
                await Settle();
                var bounds = action.TransformToAncestor(scroll).TransformBounds(new Rect(action.RenderSize));
                Assert.InRange(bounds.Top, 0, scroll.ViewportHeight);
                Assert.InRange(bounds.Bottom, 0, scroll.ViewportHeight + 1);
                Assert.True(action.Focus());
            }
            await Capture("restore");

            SelectCategory(settings, "GamesDisplaySection");
            await Settle();
            var sort = (ComboBox)settings.FindName("GamesDefaultSort");
            sort.SelectedIndex = 1;
            var bookmarks = (CheckBox)settings.FindName("GamesBookmarksOnly");
            bookmarks.IsChecked = true;
            bookmarks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.ShowFeedback("");
            await Capture("games");

            SelectCategory(settings, "StoreOffersSection");
            await Settle();
            Assert.False(((Expander)settings.FindName("PriceRefreshSetup")).IsExpanded);
            Assert.False(((Expander)settings.FindName("SteamSetup")).IsExpanded);
            var priceSetup = (Expander)settings.FindName("PriceRefreshSetup");
            priceSetup.IsExpanded = true;
            apiKey.Password = "synthetic unsubmitted entry";
            steamKey.Password = "synthetic unsubmitted entry";
            SelectCategory(settings, "ApplicationUpdatesSection");
            await Settle();
            var automatic = (CheckBox)settings.FindName("Automatic");
            automatic.IsChecked = false;
            automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.ShowFeedback("");
            await Capture("updates");
            SelectCategory(settings, "StoreOffersSection");
            await Settle();
            Assert.True(priceSetup.IsExpanded);
            Assert.Equal("synthetic unsubmitted entry", apiKey.Password);
            Assert.Equal("synthetic unsubmitted entry", steamKey.Password);
            apiKey.Clear(); steamKey.Clear();
            var keyRow = Assert.IsType<Grid>(apiKey.Parent);
            var keyActions = keyRow.Children.OfType<Button>().OrderBy(Grid.GetColumn).ToArray();
            Assert.Equal(2, keyActions.Length);
            var inputBounds = apiKey.TransformToAncestor(keyRow).TransformBounds(new Rect(apiKey.RenderSize));
            var saveBounds = keyActions[0].TransformToAncestor(keyRow).TransformBounds(new Rect(keyActions[0].RenderSize));
            var removeBounds = keyActions[1].TransformToAncestor(keyRow).TransformBounds(new Rect(keyActions[1].RenderSize));
            Assert.True(inputBounds.Right <= saveBounds.Left);
            Assert.True(saveBounds.Right <= removeBounds.Left);
            Assert.InRange(removeBounds.Right, 0, keyRow.ActualWidth + 0.5);
            await Capture("accounts");

            SelectCategory(settings, "SupportSection");
            await Settle();
            Assert.True(((FrameworkElement)settings.FindName("SupportSection")).IsVisible);
            Assert.Equal("About", ((TextBlock)settings.FindName("CategoryHeading")).Text);
            var footer = (TextBlock)settings.FindName("BuildFooter");
            Assert.True(footer.IsVisible);
            Assert.InRange(footer.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(footer.RenderSize)).Bottom, 0, height - 16);
            SelectCategory(settings, "GamesDisplaySection");
            Assert.Equal(1, sort.SelectedIndex);
            Assert.True(bookmarks.IsChecked);
            var preferences = new AppSettings(database.Options);
            Assert.Equal(1, preferences.GamesDefaultSort);
            Assert.True(preferences.GamesBookmarksOnly);
            Assert.Equal("false", preferences.Get("updates.automatic"));
            using var check = database.CreateContext();
            Assert.Empty(check.Sessions);
            Assert.Empty(check.Payouts);
            apiKey.Password = "synthetic unsubmitted entry";
            steamKey.Password = "synthetic unsubmitted entry";
        }
        finally { window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Empty(apiKey.Password);
        Assert.Empty(steamKey.Password);
    });

    internal static void SelectCategory(SettingsView view, string sectionName)
    {
        var categories = (ListBox)view.FindName("SettingsCategories");
        categories.SelectedItem = categories.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, sectionName));
    }

    private static void Save(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SettingsOverviewVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
