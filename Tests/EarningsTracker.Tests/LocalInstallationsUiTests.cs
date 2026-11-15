using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Utilities;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class LocalInstallationsUiTests(WpfTestHost host)
{
    [Fact]
    public Task BrowserShowsOutsideCatalogTitlesAndFiltersBySourceAndName() => host.RunAsync(async () =>
    {
        var window = new LocalInstallationsWindow(new(true,
        [
            new("Steam Game", LocalGameInstallations.SteamSource, true, 10),
            new("Outside Catalog", LocalGameInstallations.WindowsSource, false),
            new("Portable Game", LocalGameInstallations.FolderSource, true)
        ], [10]))
        { Width = 420, Height = 390, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var list = (ListBox)window.FindName("Installations");
            var sources = (ComboBox)window.FindName("SourceFilter");
            var search = (TextBox)window.FindName("Search");
            var status = (TextBlock)window.FindName("ResultStatus");
            Assert.Equal(3, list.Items.Count);
            Assert.True(search.IsKeyboardFocused);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.True(list.ActualHeight > 100);
            sources.SelectedItem = LocalGameInstallations.WindowsSource;
            Assert.Equal("Outside Catalog", Assert.IsType<LocalInstallation>(Assert.Single(list.Items)).Name);
            search.Text = "missing";
            Assert.Empty(list.Items);
            Assert.Contains("No titles match", status.Text);
            search.Text = "outside";
            Assert.Single(list.Items);

            sources.SelectedIndex = 0;
            search.Clear();
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "local-installations-420.png"));
            png.Save(output);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BrowserDistinguishesPartialScanFromNoMatches() => host.RunAsync(() =>
    {
        var window = new LocalInstallationsWindow(new(true, [], [])
        {
            IncompleteSources = [LocalGameInstallations.SteamSource, LocalGameInstallations.WindowsSource]
        });
        var warning = (TextBlock)window.FindName("ScanWarning");
        var status = (TextBlock)window.FindName("ResultStatus");
        Assert.Equal(Visibility.Visible, warning.Visibility);
        Assert.Contains("Steam, Windows installed apps", warning.Text);
        Assert.Contains("Results may be incomplete", warning.Text);
        Assert.Contains("locations checked", status.Text);
        return Task.CompletedTask;
    });
}
