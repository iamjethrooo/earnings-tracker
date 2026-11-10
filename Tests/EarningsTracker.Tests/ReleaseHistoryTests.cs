using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Utilities;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

public class ReleaseHistoryTests
{
    [Fact]
    public void BundledHistoryIncludesEveryPublishedVersionAndSeparatesPendingChanges()
    {
        var entries = ReleaseHistory.Load("1.2.0");
        Assert.Equal(new[] { "1.4.0", "1.3.0", "1.2.0", "1.1.0", "1.0.0" }, entries.Where(entry => !entry.Unreleased).Select(entry => entry.Version));
        var pending = Assert.Single(entries, entry => entry.Unreleased);
        Assert.Equal("Not yet published", pending.DateLabel);
        var installed = Assert.Single(entries, entry => entry.IsInstalled);
        Assert.Equal("1.2.0", installed.Version);
        Assert.True(installed.IsExpanded);
        Assert.All(entries.Where(entry => entry != installed), entry => Assert.False(entry.IsExpanded));
        Assert.All(entries.Where(entry => !entry.Unreleased), entry => Assert.Contains("UTC", entry.DateLabel));
        Assert.Contains("before", entries.Single(entry => entry.Version == "1.1.0").UpgradeNotes.Last());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9.0.0")]
    public void DevelopmentAndUnknownVersionsNeverMarkAnotherReleaseInstalled(string? version)
        => Assert.All(ReleaseHistory.Load(version), entry => { Assert.False(entry.IsInstalled); Assert.False(entry.IsExpanded); });

    [Fact]
    public void HistorySortsByVersionAndKeepsEntriesWithoutVerifiedDetails()
    {
        var entries = ReleaseHistory.Parse("""
            [{"version":"1.2.0","publishedAt":"2026-01-01T00:00:00Z","summary":"Older"},
             {"version":"1.10.0","publishedAt":"2026-02-01T00:00:00Z","summary":"Newer"}]
            """, "1.10.0");
        Assert.Equal("1.10.0", entries[0].Version);
        Assert.True(entries[0].HasNoDetails);
        Assert.Equal("Installed", entries[0].Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"version\":\"1.0.0\",\"summary\":\"Missing date\"}]")]
    [InlineData("[{\"unreleased\":true,\"version\":\"1.0.0\",\"summary\":\"Not published\"}]")]
    [InlineData("[{\"version\":\"1.0.0\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"summary\":\"First\"},{\"version\":\"1.0.0\",\"publishedAt\":\"2026-01-01T00:00:00Z\",\"summary\":\"Duplicate\"}]")]
    public void InvalidHistoryIsRejected(string json)
        => Assert.Throws<InvalidDataException>(() => ReleaseHistory.Parse(json, null));
}

[Collection("Wpf")]
public class ReleaseHistoryWindowTests(WpfTestHost host)
{
    [Theory]
    [InlineData(420, 400)]
    [InlineData(720, 640)]
    [InlineData(960, 800)]
    public Task AllVersionsCanBeExpandedAndScrolledInBoundedDialog(double width, double height) => host.RunAsync(async () =>
    {
        var window = new ReleaseHistoryWindow("1.4.0", ReleaseHistory.Load)
        { Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var list = (ListBox)window.FindName("Releases");
            var entries = list.Items.Cast<ReleaseHistoryEntry>().ToList();
            Assert.Equal(5, entries.Count(entry => !entry.Unreleased));
            Assert.Single(entries, entry => entry.Unreleased);
            var installed = (ReleaseHistoryEntry)list.SelectedItem;
            Assert.True(installed.IsInstalled);
            Assert.True(installed.IsExpanded);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(window));
            Assert.Single(Descendants<Button>(window), button => button.IsCancel);
            Assert.Contains(Descendants<ToggleButton>(window), button => button.Focusable && button.IsTabStop);
            Assert.All(Descendants<TextBlock>(list).Where(text => text.Text.Length > 90), text => Assert.Equal(TextWrapping.Wrap, text.TextWrapping));
            await Capture(window, $"installed-{width}");
            foreach (ReleaseHistoryEntry entry in list.Items)
            {
                list.ScrollIntoView(entry);
                list.UpdateLayout();
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(entry);
                var expander = Assert.Single(Descendants<Expander>(item));
                Assert.Equal(entry.AccessibleName, System.Windows.Automation.AutomationProperties.GetName(expander));
                expander.IsExpanded = true;
                Assert.True(entry.IsExpanded);
            }
            list.ScrollIntoView(list.Items[^1]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var scroller = Descendants<ScrollViewer>(list).First();
            Assert.True(scroller.ScrollableHeight > 0);
            Assert.True(scroller.VerticalOffset > 0);
            Assert.Equal(0, scroller.ScrollableWidth);
            scroller.ScrollToBottom();
            await Capture(window, $"oldest-{width}");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LoadFailureAndEmptyHistoryStillAllowClosing() => host.RunAsync(() =>
    {
        foreach (var fail in new[] { true, false })
        {
            var window = new ReleaseHistoryWindow(null, _ => fail ? throw new IOException("Synthetic missing resource") : []);
            try
            {
                var status = ((TextBlock)window.FindName("HistoryStatus")).Text;
                Assert.Contains(fail ? "could not be loaded" : "No release notes", status);
                Assert.Single(((Grid)window.Content).Children.OfType<Button>(), button => button.IsCancel);
            }
            finally { window.Close(); }
        }
        return Task.CompletedTask;
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task Capture(Window window, string name)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "ReleaseHistoryVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
