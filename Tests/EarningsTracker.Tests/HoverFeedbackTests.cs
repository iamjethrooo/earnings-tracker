using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class HoverFeedbackTests(WpfTestHost host)
{
    private const string Explanation = "Verified sessions in Current payout, plus historical sessions after the latest Paid or Processing payment day. Sessions assigned to an inactive or hidden payment return to Unpaid verified earnings. Unknown posted dates and separate adjustments remain excluded.";

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.5)]
    public Task LongHelpWrapsInTheSharedTooltipWithoutChangingContent(bool chartStyle, double scale) => host.RunAsync(async () =>
    {
        var tip = new ToolTip { Content = Explanation };
        if (chartStyle) tip.Style = (Style)Application.Current.FindResource("ChartToolTip");
        Render(tip, $"{(chartStyle ? "chart" : "plain")}-long-{scale:0.0}", scale);
        Assert.True(tip.ActualWidth <= 360, $"Tooltip width: {tip.ActualWidth}");
        var text = Assert.Single(Descendants<TextBlock>(tip));
        Assert.Equal(Explanation, text.Text);
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.True(text.ActualHeight > tip.FontSize * 3);
        Assert.Same(Application.Current.FindResource("DefaultTextColor"), text.Foreground);
        Assert.Same(Application.Current.FindResource("AppFont"), tip.FontFamily);
        Assert.Equal((double)Application.Current.FindResource("FontSizeCompactBody"), tip.FontSize);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    });

    [Fact]
    public Task ExplicitAndTemplatedContentKeepTheirMeaningAndSemanticColors() => host.RunAsync(() =>
    {
        var content = new TextBlock { Text = "Saved source: " + new string('W', 150) + "\nSeparate adjustment: -$2.501234", Foreground = Brushes.Orange };
        var tip = new ToolTip { Content = content };
        Render(tip, "explicit-content", 1);
        Assert.Same(content, tip.Content);
        Assert.Equal(Brushes.Orange, content.Foreground);
        Assert.Equal(TextWrapping.Wrap, content.TextWrapping);
        Assert.True(tip.ActualWidth <= 360);
        Assert.True(content.ActualHeight > 50);
        var template = new DataTemplate();
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.TextProperty, "Custom source evidence");
        template.VisualTree = label;
        tip = new ToolTip { Content = new object(), ContentTemplate = template };
        Render(tip, "templated-content", 1);
        Assert.Equal("Custom source evidence", Assert.Single(Descendants<TextBlock>(tip)).Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpenPopupKeepsPlacementTargetBindingAndDoesNotTakeFocus() => host.RunAsync(async () =>
    {
        var button = new Button { Content = "Hover details", Width = 120, Height = 36, DataContext = Explanation };
        var window = new Window { Content = button, Width = 1000, Height = 600, Left = -10000, Top = -10000, ShowInTaskbar = false };
        var tip = new ToolTip { PlacementTarget = button, Placement = PlacementMode.Bottom };
        tip.SetBinding(FrameworkElement.DataContextProperty, new System.Windows.Data.Binding("PlacementTarget.DataContext") { Source = tip });
        var text = new TextBlock(); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding()); tip.Content = text;
        try
        {
            window.Show(); button.Focus();
            tip.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Explanation, text.Text);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.True(button.IsKeyboardFocused);
            Assert.False(tip.Focusable);
            Assert.True(tip.ActualWidth <= 360);
            button.DataContext = "Updated evidence";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.Equal("Updated evidence", text.Text);
        }
        finally { tip.IsOpen = false; window.Close(); }
    });

    private static void Render(FrameworkElement element, string name, double scale)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize)); element.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "HoverFeedbackVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); png.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
