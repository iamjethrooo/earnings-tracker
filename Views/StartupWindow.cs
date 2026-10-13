using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nerve_earnings_tracker.Views;

internal sealed class StartupWindow : Window
{
    internal bool InitializationFinished { get; set; }

    internal StartupWindow()
    {
        Title = "Nerve Earnings Tracker";
        Width = 440; Height = 180;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)FindResource("DefaultBackgroundColor");
        Foreground = (Brush)FindResource("DefaultTextColor");
        FontFamily = TryFindResource("AppFont") as FontFamily ?? SystemFonts.MessageFontFamily;
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Nerve Earnings Tracker", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Preparing your tracker…", FontSize = (double)FindResource("FontSizeBody"), Margin = new Thickness(0, 14, 0, 10),
            Foreground = (Brush)FindResource("SecondaryTextColor") });
        content.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4,
            Foreground = (Brush)FindResource("AccentColor"), Background = (Brush)FindResource("DefaultBorderColor") });
        Content = new Border { Padding = new Thickness(28), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("DefaultBorderColor"), Child = content };
        // Initialization owns writes; detaching the progress window cannot cancel them safely.
        Closing += (_, e) => e.Cancel = !InitializationFinished;
    }
}
