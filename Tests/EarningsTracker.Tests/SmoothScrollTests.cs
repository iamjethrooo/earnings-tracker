using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Controls;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class SmoothScrollTests(WpfTestHost host)
{
    [Fact]
    public Task WheelAccumulatesReversesAndStopsAtItsDestination() => host.RunAsync(async () =>
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 5000 } };
        await Layout(viewer);
        Assert.True(SmoothScroll.GetIsEnabled(viewer));
        if (!AnimationAvailable()) { Assert.False(Wheel(viewer, -120).Handled); return; }
        try
        {
            Assert.True(Wheel(viewer, -120).Handled);
            Assert.True(Wheel(viewer, -120).Handled);
            await Finish(viewer);
            double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            Assert.Equal(step * 2, viewer.VerticalOffset, 1);
            Assert.True(Wheel(viewer, -120).Handled);
            Assert.True(Wheel(viewer, 120).Handled);
            await Finish(viewer);
            Assert.Equal(step, viewer.VerticalOffset, 1);
            double settled = viewer.VerticalOffset;
            await Finish(viewer);
            Assert.Equal(settled, viewer.VerticalOffset);
        }
        finally { SmoothScroll.SetIsEnabled(viewer, false); }
    });

    [Theory]
    [InlineData(ScrollUnit.Pixel, false)]
    [InlineData(ScrollUnit.Item, false)]
    [InlineData(ScrollUnit.Pixel, true)]
    [InlineData(ScrollUnit.Item, true)]
    public Task RealListPreservesUnitsAndRecycling(ScrollUnit unit, bool table) => host.RunAsync(async () =>
    {
        ListBox list = table ? new ListView { View = new GridView() } : new ListBox();
        if (list is ListView { View: GridView grid })
        {
            grid.ColumnHeaderContainerStyle = (Style)Application.Current.FindResource("TableColumnHeader");
            grid.Columns.Add(new GridViewColumn { Header = "Game", DisplayMemberBinding = new System.Windows.Data.Binding("."), Width = 300 });
        }
        list.Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor");
        list.Foreground = (Brush)Application.Current.FindResource("DefaultTextColor");
        list.ItemsSource = Enumerable.Range(0, 1200).Select(i => $"Game {i}");
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, unit);
        var window = new Window
        {
            Content = list, Width = 480, Height = 240, ShowInTaskbar = false,
            WindowStyle = WindowStyle.None, Left = -10000, Top = -10000
        };
        window.Show();
        await Layout(list);
        var viewer = Descendants<ScrollViewer>(list).First();
        try
        {
            Assert.True(SmoothScroll.GetIsEnabled(viewer));
            var wheel = Wheel(viewer, -120);
            Assert.Equal(unit == ScrollUnit.Pixel && AnimationAvailable(), wheel.Handled);
            await Finish(list);
            Assert.True(viewer.CanContentScroll);
            Assert.Equal(unit, VirtualizingPanel.GetScrollUnit(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            Assert.InRange(Descendants<VirtualizingStackPanel>(list).Single().Children.Count, 1, 100);
            if (unit == ScrollUnit.Pixel && AnimationAvailable()) Assert.True(viewer.VerticalOffset > 0);
            // Render the actual styled/recycled list after animation without starting App.
            var bitmap = new RenderTargetBitmap(480, 240, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(list);
            var directory = Path.Combine(AppContext.BaseDirectory, "SmoothScrollVisuals");
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"{(table ? "table" : "list")}-{unit}.png"));
            encoder.Save(file);
        }
        finally { SmoothScroll.SetIsEnabled(viewer, false); window.Close(); }
    });

    [Fact]
    public Task ExtentChangesDoNotCutOffPendingWheelMovement() => host.RunAsync(async () =>
    {
        var content = new Border { Height = 5000 };
        var viewer = new ScrollViewer { Content = content };
        await Layout(viewer);
        if (!AnimationAvailable()) return;
        try
        {
            Assert.True(Wheel(viewer, -120).Handled);
            content.Height += 500;
            await Layout(viewer);
            await Finish(viewer);
            double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            Assert.Equal(step, viewer.VerticalOffset, 1);
        }
        finally { SmoothScroll.SetIsEnabled(viewer, false); }
    });

    [Fact]
    public Task WheelPacketsAcrossFramesKeepTheAccumulatedDestination() => host.RunAsync(async () =>
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 5000 } };
        await Layout(viewer);
        if (!AnimationAvailable()) return;
        try
        {
            for (int packet = 0; packet < 4; packet++)
            {
                Assert.True(Wheel(viewer, -120).Handled);
                await Task.Delay(20);
            }
            await Finish(viewer);
            double step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            Assert.Equal(step * 4, viewer.VerticalOffset, 1);
        }
        finally { SmoothScroll.SetIsEnabled(viewer, false); }
    });

    [Fact]
    public Task NestedViewerHighResolutionAndCancellationRetainNativeControl() => host.RunAsync(async () =>
    {
        var inner = new ScrollViewer { Height = 120, Content = new Border { Height = 3000 } };
        var panel = new StackPanel();
        panel.Children.Add(inner);
        panel.Children.Add(new Border { Height = 3000 });
        var outer = new ScrollViewer { Content = panel };
        await Layout(outer);
        try
        {
            Assert.False(Wheel(inner, -30).Handled);
            Assert.False(Wheel(outer, 120).Handled); // Already at the upper edge.
            if (!AnimationAvailable()) return;
            Assert.True(Wheel(inner, -120).Handled);
            await Finish(outer);
            Assert.True(inner.VerticalOffset > 0);
            Assert.Equal(0, outer.VerticalOffset);

            Assert.True(Wheel(outer, -120).Handled);
            outer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                new FakePresentationSource(), Environment.TickCount, Key.Down)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Finish(outer);
            Assert.Equal(0, outer.VerticalOffset);

            Assert.True(Wheel(outer, -120).Handled);
            outer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            await Finish(outer);
            Assert.Equal(0, outer.VerticalOffset);
            Assert.True(Wheel(outer, -120).Handled);
            SmoothScroll.SetIsEnabled(outer, false);
            await Finish(outer);
            Assert.Equal(0, outer.VerticalOffset);
            Assert.False(Wheel(outer, -120).Handled);
        }
        finally
        {
            SmoothScroll.SetIsEnabled(inner, false);
            SmoothScroll.SetIsEnabled(outer, false);
        }
    });

    private static bool AnimationAvailable() => SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0;

    private static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(args);
        return args;
    }

    private static async Task Layout(FrameworkElement element)
    {
        element.Measure(new Size(480, 240));
        element.Arrange(new Rect(0, 0, 480, 240));
        element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static async Task Finish(FrameworkElement element)
    {
        await Task.Delay(250);
        await Layout(element);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class FakePresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
