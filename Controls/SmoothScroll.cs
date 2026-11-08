using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EarningsTracker.Controls;

/// <summary>Short wheel easing for pixel viewports, without changing their scrolling or virtualization units.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ScrollState), typeof(SmoothScroll));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not ScrollViewer viewer) return;
        if (viewer.GetValue(StateProperty) is ScrollState previous) previous.Detach();
        viewer.SetValue(StateProperty, (bool)e.NewValue ? new ScrollState(viewer) : null);
    }

    private sealed class ScrollState
    {
        private const double DurationMilliseconds = 100;
        private readonly ScrollViewer _viewer;
        private readonly Stopwatch _elapsed = new();
        private bool _isAnimating;
        private TimeSpan? _lastRenderTime;
        private double _position;
        private double _target;

        public ScrollState(ScrollViewer viewer)
        {
            _viewer = viewer;
            viewer.PreviewMouseWheel += Wheel;
            viewer.PreviewKeyDown += KeyDown;
            viewer.PreviewMouseDown += MouseDown;
            viewer.Unloaded += Unloaded;
            viewer.ScrollChanged += ScrollChanged;
        }

        public void Detach()
        {
            Stop();
            _viewer.PreviewMouseWheel -= Wheel;
            _viewer.PreviewKeyDown -= KeyDown;
            _viewer.PreviewMouseDown -= MouseDown;
            _viewer.Unloaded -= Unloaded;
            _viewer.ScrollChanged -= ScrollChanged;
        }

        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            // Ancestor preview handlers must leave nested viewers and input controls in charge.
            for (var node = e.OriginalSource as DependencyObject; node != null && node != _viewer;
                 node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            {
                if (node is ScrollViewer or TextBoxBase or ComboBox) return;
            }

            // High-resolution wheel packets retain native handling; do not add inertia to them.
            if (!SystemParameters.ClientAreaAnimation || e.Delta == 0 || e.Delta % 120 != 0 ||
                Keyboard.Modifiers != ModifierKeys.None || !UsesPixels())
            {
                Stop();
                return;
            }

            int lines = SystemParameters.WheelScrollLines;
            if (lines == 0) { Stop(); return; }
            double distance = lines < 0 ? _viewer.ViewportHeight : lines * 16.0;
            bool reversing = _isAnimating && Math.Sign(_target - _position) == Math.Sign(e.Delta);
            double origin = _isAnimating && !reversing ? _target : _viewer.VerticalOffset;
            double target = Math.Clamp(origin - e.Delta / 120.0 * distance, 0, _viewer.ScrollableHeight);
            if (Math.Abs(target - _viewer.VerticalOffset) < 0.1) { Stop(); return; }

            _target = target;
            if (!_isAnimating)
            {
                _position = _viewer.VerticalOffset;
                _elapsed.Restart();
                _isAnimating = true;
                CompositionTarget.Rendering += RenderFrame;
            }
            e.Handled = true;
        }

        private bool UsesPixels()
        {
            if (!_viewer.CanContentScroll) return true;
            // Logical item offsets must never be interpreted as pixels. Inspect only the
            // owning items panel, not every realized row or a nested viewport.
            if (_viewer.Content is not ItemsPresenter presenter) return false;
            presenter.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(presenter) == 0 ||
                VisualTreeHelper.GetChild(presenter, 0) is not VirtualizingStackPanel panel) return false;
            var owner = ItemsControl.GetItemsOwner(panel);
            return owner != null && VirtualizingPanel.GetScrollUnit(owner) == ScrollUnit.Pixel;
        }

        private void RenderFrame(object? sender, EventArgs e)
        {
            if (!SystemParameters.ClientAreaAnimation) { Stop(); return; }
            // WPF can raise Rendering more than once for the same frame after layout changes.
            if (e is RenderingEventArgs frame)
            {
                if (_lastRenderTime == frame.RenderingTime) return;
                _lastRenderTime = frame.RenderingTime;
            }

            // Follow the destination continuously instead of restarting an ease-out on every
            // wheel packet. Elapsed-time weighting keeps the response independent of refresh rate;
            // 99.9% of the remaining distance is covered within the configured settling interval.
            double weight = 1 - Math.Exp(-Math.Log(1000) * _elapsed.Elapsed.TotalMilliseconds / DurationMilliseconds);
            _elapsed.Restart();
            _target = Math.Clamp(_target, 0, _viewer.ScrollableHeight);
            _position += (_target - _position) * weight;
            bool finished = Math.Abs(_target - _position) < 0.1;
            if (finished) _position = _target;
            _viewer.ScrollToVerticalOffset(Math.Clamp(_position, 0, _viewer.ScrollableHeight));
            if (finished) Stop();
        }

        private void Stop()
        {
            CompositionTarget.Rendering -= RenderFrame;
            _isAnimating = false;
            _lastRenderTime = null;
            _elapsed.Reset();
        }
        private void KeyDown(object sender, KeyEventArgs e) => Stop();
        private void MouseDown(object sender, MouseButtonEventArgs e) => Stop();
        private void Unloaded(object sender, RoutedEventArgs e) => Stop();
        private void ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.OriginalSource != _viewer) return;
            if (e.ViewportHeightChange != 0) Stop();
            // Virtualized variable-height rows can revise the estimated extent mid-scroll.
            // Keep moving toward the destination rather than cutting every such scroll short.
            else if (_isAnimating && e.ExtentHeightChange != 0)
                _target = Math.Clamp(_target, 0, _viewer.ScrollableHeight);
        }
    }
}
