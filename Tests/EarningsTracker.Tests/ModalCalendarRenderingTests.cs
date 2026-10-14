using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Controls;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class ModalCalendarRenderingTests(WpfTestHost host)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ModalPreservesDisabledListBackgrounds(bool useListView) => host.RunAsync(async () =>
    {
        var columns = new GridView { ColumnHeaderContainerStyle = (Style)Application.Current.FindResource("TableColumnHeader") };
        columns.Columns.Add(new GridViewColumn { Header = "Amount", Width = 160 });
        ListBox page = useListView ? new ListView { View = columns } : new ListBox();
        page.Background = Brushes.Transparent;
        page.BorderThickness = new Thickness(0);
        var window = new MainWindow(_ => new UserControl { Content = page });
        var modal = (Modal)window.FindName("MainModal");
        try
        {
            var root = (FrameworkElement)window.Content;
            modal.Open("Test dialog", new TextBlock { Text = "The page remains visible under the backdrop." });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var bitmap = Render(root, 1000, 600, useListView ? "modal-listview-backdrop.png" : "modal-backdrop.png");
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(950, 550, 1, 1), pixel, 4, 0);
            Assert.False(page.IsEnabled);
            // Disabled list templates must preserve the charcoal canvas beneath the backdrop.
            Assert.InRange(pixel[0], (byte)0, (byte)4);
            Assert.InRange(pixel[1], (byte)0, (byte)4);
            Assert.InRange(pixel[2], (byte)0, (byte)4);
            bitmap.CopyPixels(new Int32Rect(15, 500, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[0], (byte)4, (byte)7);
            Assert.InRange(pixel[1], (byte)3, (byte)6);
            Assert.InRange(pixel[2], (byte)3, (byte)6);
            bitmap.CopyPixels(new Int32Rect(900, 350, 1, 1), pixel, 4, 0);
            Assert.InRange(pixel[0], (byte)34, (byte)38);
            Assert.InRange(pixel[1], (byte)30, (byte)34);
            Assert.InRange(pixel[2], (byte)30, (byte)34);
            Assert.True(ScrollViewer.GetCanContentScroll(page));
            if (useListView)
                Assert.Contains(Descendants<GridViewColumnHeader>(page), header => header.Content as string == "Amount");
            modal.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(page.IsEnabled);
        }
        finally { modal.Close(); window.Close(); }
    });

    [Fact]
    public Task CalendarNavigationRendersChevronsAndChangesMonth() => host.RunAsync(async () =>
    {
        var picker = new DatePicker { DisplayDate = new DateTime(2026, 6, 5),
            Style = (Style)Application.Current.FindResource(typeof(DatePicker)) };
        picker.ApplyTemplate();
        var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
        var calendar = Assert.IsType<Calendar>(popup.Child);
        Render(calendar, 300, 300, "calendar-navigation.png");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var item = Descendants<CalendarItem>(calendar).Single();
        foreach (var name in new[] { "PART_PreviousButton", "PART_NextButton" })
        {
            var button = (Button)item.Template.FindName(name, item);
            var arrow = Assert.Single(Descendants<System.Windows.Shapes.Path>(button));
            Assert.True(arrow.ActualWidth > 0 && arrow.ActualHeight > 0);
            Assert.Same(button.Foreground, arrow.Stroke);
            var bitmap = Render(button, 32, 30, name + ".png");
            var pixels = new byte[32 * 30 * 4];
            bitmap.CopyPixels(pixels, 32 * 4, 0);
            Assert.Contains(Enumerable.Range(0, 32 * 30), i => pixels[i * 4 + 3] > 0 && pixels[i * 4] > 100);
            var previous = calendar.DisplayDate;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(previous.AddMonths(name == "PART_PreviousButton" ? -1 : 1).Month, calendar.DisplayDate.Month);
        }
    });

    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height, string name)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        bitmap.Render(visual);
        var directory = Path.Combine(AppContext.BaseDirectory, "rendered-ui");
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name));
        encoder.Save(stream);
        return bitmap;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
