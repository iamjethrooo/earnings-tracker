using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using EarningsTracker.ViewModels;

namespace EarningsTracker.Controls;

// Daily amounts use separate bars, including signed adjustments; they are not a running balance.
public sealed class DashboardEarningsChart : Canvas
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(nameof(Days),
        typeof(IReadOnlyList<DashboardDay>), typeof(DashboardEarningsChart), new PropertyMetadata(null, Changed));
    public IReadOnlyList<DashboardDay>? Days { get => (IReadOnlyList<DashboardDay>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public DashboardEarningsChart()
    {
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Daily earned and pending USD. Exact values and adjustments are available in Daily values.");
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((DashboardEarningsChart)sender).Draw();
    internal void Draw()
    {
        Children.Clear();
        if (Days is not { Count: > 0 } days || ActualWidth < 100 || ActualHeight < 80) return;
        var labels = (Brush)FindResource("SecondaryTextColor");
        var grid = (Brush)FindResource("DefaultBorderColor");
        var accent = (Brush)FindResource("AccentColor");
        var pending = (Brush)FindResource("PendingTextColor");
        var maximum = Math.Max(1m, decimal.Ceiling(days.Max(day => Math.Max(day.Earned, day.Pending))));
        var minimum = Math.Min(0m, decimal.Floor(days.Min(day => Math.Min(day.Earned, day.Pending))));
        var ticks = Enumerable.Range(0, 5).Select(i => minimum + (maximum - minimum) * i / 4m).ToArray();
        var tickLabels = ticks.Select(amount => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"))).ToArray();
        var font = (FontFamily)FindResource("AppFont");
        var left = tickLabels.Select(text =>
        {
            var label = new TextBlock { Text = text, FontFamily = font, FontSize = 12 };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return label.DesiredSize.Width;
        }).Max() + 12;
        const double top = 12, right = 24, bottom = 30;
        var width = ActualWidth - left - right;
        if (width <= 0) return;
        var height = ActualHeight - top - bottom;
        var slot = width / days.Count;
        double X(int i) => left + (i + 0.5) * slot;
        double Y(decimal value) => top + (double)((maximum - value) / (maximum - minimum)) * height;
        for (var i = 0; i < ticks.Length; i++)
        {
            var y = Y(ticks[i]);
            Children.Add(new Line { X1 = left, X2 = ActualWidth - right, Y1 = y, Y2 = y, Stroke = grid });
            Label(tickLabels[i], 0, y - 8, left - 10, TextAlignment.Right);
        }
        var barWidth = Math.Min(24, slot * 0.34);
        for (var i = 0; i < days.Count; i++)
        {
            AddBar(days[i].Earned, X(i) - barWidth, accent);
            AddBar(days[i].Pending, X(i), pending);
        }
        void AddBar(decimal value, double x, Brush brush)
        {
            var bar = new Rectangle { Width = barWidth, Height = Math.Max(1, Math.Abs(Y(value) - Y(0))), Fill = brush, IsHitTestVisible = false };
            SetLeft(bar, x); SetTop(bar, Math.Min(Y(value), Y(0))); Children.Add(bar);
        }
        var count = Math.Min(days.Count, Math.Clamp((int)(width / 100), 2, 7));
        for (var n = 0; n < count; n++)
        {
            var index = (int)Math.Round(n * (days.Count - 1.0) / Math.Max(1, count - 1));
            Label(days[index].Date.Date == DateTime.Today ? "Today*" : days[index].Date.ToString("MMM d", CultureInfo.InvariantCulture), X(index) - 24, ActualHeight - 20, 48);
        }
        for (var i = 0; i < days.Count; i++)
        {
            var target = new Rectangle { Width = slot, Height = height, Fill = Brushes.Transparent,
                ToolTip = new ToolTip { Style = (Style)FindResource("ChartToolTip"), Content = days[i].Description } };
            SetLeft(target, Math.Clamp(X(i) - target.Width / 2, left, Math.Max(left, ActualWidth - right - target.Width)));
            SetTop(target, top); Children.Add(target);
        }
        void Label(string text, double x, double y, double labelWidth, TextAlignment alignment = TextAlignment.Center)
        {
            var label = new TextBlock { Text = text, FontFamily = font, FontSize = 12, Foreground = labels, Width = labelWidth, TextAlignment = alignment };
            SetLeft(label, x); SetTop(label, y); Children.Add(label);
        }
    }
}
