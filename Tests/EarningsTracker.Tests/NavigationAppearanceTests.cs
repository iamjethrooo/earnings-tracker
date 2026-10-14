using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentIcons.Wpf;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class NavigationAppearanceTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1000, 600)]
    [InlineData(1200, 720)]
    [InlineData(1600, 900)]
    public Task SidebarAndScrollbarsRenderWithoutClipping(int width, int height) => host.RunAsync(async () =>
    {
        // Supply a synthetic page so no production context or startup is involved.
        var scroll = new ScrollViewer
        {
            Margin = new Thickness(20),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Width = 1800, Height = 1200, Padding = new Thickness(24),
                Background = (Brush)Application.Current.FindResource("SurfaceColor"),
                Child = new TextBlock { Text = "Scrollbar preview", Style = (Style)Application.Current.FindResource("SectionHeading") }
            }
        };
        var window = new MainWindow(_ => new UserControl { Content = scroll })
        {
            Width = width, Height = height, ShowInTaskbar = false, Left = -10000, Top = -10000
        };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var nav = (ListBox)window.FindName("NavBar");
            var settings = (ListBox)window.FindName("SettingsNav");
            var items = nav.Items.Cast<ListBoxItem>().Concat(settings.Items.Cast<ListBoxItem>()).ToArray();
            var icons = items.Select(item => (SymbolIcon)item.Template.FindName("NavigationIcon", item)).ToArray();
            Assert.Equal(6, icons.Select(icon => icon.Symbol).Distinct().Count());
            foreach (var item in items)
            {
                var icon = (SymbolIcon)item.Template.FindName("NavigationIcon", item);
                var label = Descendants<TextBlock>(item).Single(text => text.Text == (string)item.Content);
                var iconBounds = icon.TransformToAncestor(item).TransformBounds(new Rect(icon.RenderSize));
                var labelBounds = label.TransformToAncestor(item).TransformBounds(new Rect(label.RenderSize));
                Assert.True(iconBounds.Right < labelBounds.Left);
                Assert.True(labelBounds.Right <= item.ActualWidth);
                Assert.Same(item.Foreground, icon.Foreground);
                Assert.False(icon.Focusable);
            }
            Assert.Equal(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
            Assert.Equal(Visibility.Visible, scroll.ComputedHorizontalScrollBarVisibility);
            foreach (var bar in Descendants<ScrollBar>(scroll))
            {
                Assert.Same(Application.Current.FindResource(typeof(ScrollBar)), bar.Style);
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                Assert.Same(Application.Current.FindResource("VerticalScrollThumb"), track.Thumb.Style);
                var thumbBorder = (Border)track.Thumb.Template.FindName("ThumbBorder", track.Thumb);
                Assert.Equal(6, bar.Orientation == Orientation.Vertical ? thumbBorder.ActualWidth : thumbBorder.ActualHeight);
            }
            scroll.ScrollToVerticalOffset(100);
            scroll.ScrollToHorizontalOffset(100);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(100, scroll.VerticalOffset);
            Assert.Equal(100, scroll.HorizontalOffset);
            Capture((FrameworkElement)window.Content, $"sidebar-scrollbars-{width}x{height}");
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement element, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "NavigationVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
