using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EarningsTracker.Prototypes;

// Uses the existing Statistics Canvas/Polyline pattern, without constructing the production view.
public sealed class EarningsChart : Canvas
{
    public static readonly DependencyProperty DaysProperty = DependencyProperty.Register(nameof(Days),
        typeof(IReadOnlyList<TrendDay>), typeof(EarningsChart), new PropertyMetadata(null));
    public static readonly DependencyProperty FontFamilyProperty = TextBlock.FontFamilyProperty.AddOwner(typeof(EarningsChart));
    public IReadOnlyList<TrendDay>? Days { get => (IReadOnlyList<TrendDay>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public EarningsChart()
    {
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(this, "Daily verified and pending session earnings. Exact values available in the daily values list.");
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == DaysProperty) Draw();
    }

    internal void Draw()
    {
        Children.Clear();
        if (Days is not { Count: > 0 } days || ActualWidth < 100 || ActualHeight < 80) return;
        var gridBrush = (Brush)FindResource("DefaultBorderColor");
        var labels = (Brush)FindResource("SecondaryTextColor");
        var accent = (Brush)FindResource("AccentColor");
        var pending = (Brush)FindResource("PendingTextColor");
        var maximum = Math.Max(10m, decimal.Ceiling(days.Max(d => Math.Max(d.Verified, d.Pending)) / 10m) * 10m);
        var tickLabels = Enumerable.Range(0, 5).Select(i => $"${maximum * i / 4:0.00}").ToArray();
        var left = tickLabels.Select(text =>
        {
            var label = new TextBlock { Text = text, FontFamily = FontFamily, FontSize = 12 };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return label.DesiredSize.Width;
        }).Max() + 12;
        const double top = 12, right = 16, bottom = 30;
        var width = ActualWidth - left - right;
        var height = ActualHeight - top - bottom;
        double X(int i) => left + i * width / Math.Max(1, days.Count - 1);
        double Y(decimal amount) => top + (1 - (double)(amount / maximum)) * height;
        for (var i = 0; i <= 4; i++)
        {
            var y = Y(maximum * i / 4);
            Children.Add(new Line { X1 = left, X2 = ActualWidth - right, Y1 = y, Y2 = y, Stroke = gridBrush });
            Label(tickLabels[i], 0, y - 8, left - 10, labels, TextAlignment.Right);
        }
        var verifiedPoints = new PointCollection(days.Select((day, i) => new Point(X(i), Y(day.Verified))));
        var area = new PointCollection(verifiedPoints) { new(X(days.Count - 1), Y(0)), new(X(0), Y(0)) };
        var fill = new LinearGradientBrush(Color.FromArgb(64, 124, 131, 255), Color.FromArgb(0, 124, 131, 255), 90);
        Children.Add(new Polygon { Points = area, Fill = fill });
        Children.Add(new Polyline { Points = verifiedPoints, Stroke = accent, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round });
        Children.Add(new Polyline
        {
            Points = new PointCollection(days.Select((day, i) => new Point(X(i), Y(day.Pending)))),
            Stroke = pending, StrokeThickness = 1.5, StrokeDashArray = [3, 3]
        });
        var labelCount = Math.Clamp((int)(width / 100), 2, 7);
        for (var n = 0; n < labelCount; n++)
        {
            var index = (int)Math.Round(n * (days.Count - 1.0) / (labelCount - 1));
            Label(days[index].Date.ToString("MMM d", CultureInfo.InvariantCulture), X(index) - 24, ActualHeight - 20, 48, labels);
        }
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            // A full-height target makes daily values discoverable without aiming at a thin line.
            var target = new Rectangle
            {
                Width = Math.Max(6, width / days.Count), Height = height, Fill = Brushes.Transparent,
                ToolTip = new ToolTip
                {
                    Style = (Style)FindResource("ChartToolTip"),
                    Content = $"{day.Date:MMM d, yyyy}\nVerified: {DashboardViewModel.Usd(day.Verified)}\nPending: {DashboardViewModel.Usd(day.Pending)}"
                }
            };
            SetLeft(target, Math.Clamp(X(i) - target.Width / 2, left, ActualWidth - right - target.Width));
            SetTop(target, top);
            Children.Add(target);
        }
    }

    private void Label(string text, double x, double y, double width, Brush brush, TextAlignment alignment = TextAlignment.Center)
    {
        var label = new TextBlock { Text = text, FontFamily = FontFamily, FontSize = 12, Foreground = brush, Width = width, TextAlignment = alignment };
        SetLeft(label, x); SetTop(label, y); Children.Add(label);
    }
}
