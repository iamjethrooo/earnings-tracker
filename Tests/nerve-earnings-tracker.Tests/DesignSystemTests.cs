using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Views;

namespace nerve_earnings_tracker.Tests;

[Collection("Wpf")]
public class DesignSystemTests(WpfTestHost host)
{
    [Theory]
    [InlineData("Paid", "VerifiedTextColor", "VerifiedBackgroundColor")]
    [InlineData("Processing", "PendingTextColor", "PendingBackgroundColor")]
    [InlineData("Failed", "RejectedTextColor", "RejectedBackgroundColor")]
    [InlineData("Unknown", "NotUploadedTextColor", "NotUploadedBackgroundColor")]
    public Task WorkerPaymentBadgesUseSharedStatusPaletteAndFrozenBrushes(string status, string foreground, string background) => host.RunAsync(async () =>
    {
        var row = await Task.Run(() => PayoutHistoryItem.FromPayout(new Payout { Type = "Payment", Status = status, Amount = -20 }));
        Assert.True(row.StatusForeground.IsFrozen);
        Assert.True(row.StatusBackground.IsFrozen);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource(foreground)).Color, ((SolidColorBrush)row.StatusForeground).Color);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource(background)).Color, ((SolidColorBrush)row.StatusBackground).Color);
        Assert.Equal("-$20.00", row.AmountText);
        Assert.Equal(status, row.Status);
    });

    [Theory]
    [InlineData(SessionStatus.Verified, true, "Below minimum time", "PendingTextColor")]
    [InlineData(SessionStatus.Rejected, true, "Below minimum time", "PendingTextColor")]
    [InlineData(SessionStatus.NotUploaded, true, "Not Uploaded", "NotUploadedTextColor")]
    [InlineData(SessionStatus.Verified, false, "Verified", "VerifiedTextColor")]
    [InlineData(SessionStatus.Rejected, false, "Rejected", "RejectedTextColor")]
    [InlineData(SessionStatus.Pending, false, "Pending", "DefaultTextColor")]
    public Task SharedDetailBadgesPreserveStatusAndDurationPrecedence(SessionStatus status, bool zero, string text, string brushKey) => host.RunAsync(async () =>
    {
        var label = new TextBlock
        {
            Style = (Style)Application.Current.FindResource("DetailedSessionStatusText"),
            DataContext = new Session { Status = status, Duration = zero ? TimeSpan.Zero : TimeSpan.FromMinutes(5) }
        };
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        label.Measure(new Size(300, 100));
        Assert.Equal(text, label.Text);
        Assert.Same(Application.Current.FindResource(brushKey), label.Foreground);
        label.DataContext = new Session { Status = SessionStatus.Verified, Duration = TimeSpan.FromMinutes(10) };
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        label.Measure(new Size(300, 100));
        Assert.Equal("Verified", label.Text);
        Assert.Same(Application.Current.FindResource("VerifiedTextColor"), label.Foreground);
    });

    [Fact]
    public Task ArtworkPresenterHandlesMissingCoversAndUpdatesTheExistingImage() => host.RunAsync(() =>
    {
        var artwork = new ContentControl { Style = (Style)Application.Current.FindResource("ArtworkPresenter") };
        artwork.Measure(new Size(60, 80));
        artwork.Arrange(new Rect(0, 0, 60, 80));
        artwork.UpdateLayout();
        var image = (Image)artwork.Template.FindName("CoverImage", artwork);
        Assert.Null(image.Source);
        Assert.False(artwork.Focusable);
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4);
        bitmap.Freeze();
        artwork.Content = bitmap;
        artwork.UpdateLayout();
        Assert.Same(bitmap, image.Source);
        Assert.Same(image, artwork.Template.FindName("CoverImage", artwork));
        artwork.Content = null;
        artwork.UpdateLayout();
        Assert.Null(image.Source);
        Assert.Equal(Stretch.UniformToFill, image.Stretch);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PageHostConsumesPaddingAndMarginOnceAndKeepsContentTemplates() => host.RunAsync(async () =>
    {
        var child = new Border { Width = 60, Height = 20 };
        var page = new ContentControl
        {
            Style = (Style)Application.Current.FindResource("PageHost"), Content = child,
            Margin = new Thickness(13), Padding = new Thickness(9),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        var root = new Grid();
        root.Children.Add(page);
        root.Measure(new Size(300, 200));
        root.Arrange(new Rect(0, 0, 300, 200));
        root.UpdateLayout();
        Assert.Equal(new Point(22, 22), child.TranslatePoint(new Point(), root));
        page.HorizontalContentAlignment = HorizontalAlignment.Right;
        root.UpdateLayout();
        Assert.Equal(278, child.TranslatePoint(new Point(60, 0), root).X);
        var templateText = new FrameworkElementFactory(typeof(TextBlock));
        templateText.SetBinding(TextBlock.TextProperty, new Binding());
        page.ContentTemplate = new DataTemplate { VisualTree = templateText };
        page.Content = "Template content";
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        root.UpdateLayout();
        var presenter = (ContentPresenter)VisualTreeHelper.GetChild(VisualTreeHelper.GetChild(page, 0), 0);
        Assert.Equal("Template content", ((TextBlock)VisualTreeHelper.GetChild(presenter, 0)).Text);
        var ordinary = new ContentControl { Content = "Ordinary content" };
        ordinary.Measure(new Size(100, 100));
        Assert.Null(ordinary.Style);
        Assert.Null(ordinary.Background);
        Assert.Equal(new Thickness(), ordinary.Padding);
    });

    [Fact]
    public Task SharedPaletteAndLegacyPanelKeysKeepTheirGeometry() => host.RunAsync(() =>
    {
        var dashboard = new Border { Style = (Style)Application.Current.FindResource("DashboardStatPanel") };
        var details = new Border { Style = (Style)Application.Current.FindResource("GameDetailPanel") };
        dashboard.Measure(new Size(300, 200));
        details.Measure(new Size(300, 200));
        Assert.Equal(new Thickness(20), dashboard.Padding);
        Assert.Equal(new CornerRadius(8), dashboard.CornerRadius);
        Assert.Equal(new Thickness(5, 0, 5, 0), dashboard.Margin);
        Assert.Equal(new Thickness(0, 4, 0, 4), details.Margin);
        Assert.Same(dashboard.Background, details.Background);
        Assert.Same(Application.Current.FindResource("DefaultBorderColor"), details.BorderBrush);
        var title = new TextBlock { Style = (Style)Application.Current.FindResource("TabName") };
        Assert.Equal(25, title.FontSize);
        foreach (var key in new[] { "SectionHeading", "CompactSectionHeading" })
        {
            var heading = new TextBlock { Style = (Style)Application.Current.FindResource(key) };
            heading.Measure(new Size(300, 100));
            Assert.Same(Application.Current.FindResource("DefaultTextColor"), heading.Foreground);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SharedFontResolvesToBundledInterInStandaloneWindows() => host.RunAsync(async () =>
    {
        var font = (FontFamily)Application.Current.FindResource("AppFont");
        var face = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.True(face.TryGetGlyphTypeface(out var glyphs));
        Assert.Contains("Inter-VariableFont", Uri.UnescapeDataString(glyphs.FontUri.ToString()), StringComparison.OrdinalIgnoreCase);
        var window = new Window { Width = 300, Height = 200, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(font, window.FontFamily);
        }
        finally { window.Close(); }
    });
}
