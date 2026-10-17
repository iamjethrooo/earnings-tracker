using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public sealed class ApplicationBackupViewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(740)]
    [InlineData(1000)]
    public Task BackupActionsRemainReachableAtSettingsWidths(int width) => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var view = new SettingsView(database.Options) { Width = width, Height = 560 };
        view.Measure(new Size(width, 560));
        view.Arrange(new Rect(0, 0, width, 560));
        view.UpdateLayout();
        var scroll = Assert.Single(((Grid)view.Content).Children.OfType<ScrollViewer>());
        scroll.ScrollToBottom();
        view.UpdateLayout();
        foreach (var name in new[] { "BackupButton", "RestoreButton" })
        {
            var button = Assert.IsType<Button>(view.FindName(name));
            var bounds = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
            Assert.True(button.ActualWidth > 90);
            Assert.InRange(bounds.Left, 0, width);
            Assert.InRange(bounds.Right, 0, width);
            Assert.InRange(bounds.Top, 0, scroll.ViewportHeight);
            Assert.InRange(bounds.Bottom, 0, scroll.ViewportHeight);
        }
        var bitmap = new RenderTargetBitmap(width, 560, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "BackupVisuals");
        Directory.CreateDirectory(outputDirectory);
        using var output = File.Create(Path.Combine(outputDirectory, $"settings-backup-{width}.png"));
        encoder.Save(output);
        return Task.CompletedTask;
    });
}
