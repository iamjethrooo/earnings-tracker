using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;

namespace EarningsTracker.Prototypes;

// A small executable render/interaction check; no test host or production Application is involved.
internal static class PrototypeVerification
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        using var bindingErrors = new StringWriter();
        using var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var window = new PrototypeWindow();
        var root = (FrameworkElement)window.Content;
        var vm = window.ViewModel;

        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks.Add("PASS " + description);
        }
        void Layout(double width, double height)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Pump();
            root.UpdateLayout();
            ((EarningsChart)window.FindName("TrendChart")).Draw();
            root.UpdateLayout();
        }
        void Capture(string name, int width, int height, double dpi = 96)
        {
            Layout(width, height);
            var bitmap = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }

        foreach (var (name, width, height) in new[] { ("minimum", 1000, 600), ("compact", 1200, 720), ("default", 1440, 960), ("wide", 1800, 1000) })
        {
            var scroll = (ScrollViewer)window.FindName("DashboardScroll");
            scroll.ScrollToTop();
            Capture(name, width, height);
            Check(scroll.ScrollableWidth == 0, name + ": no horizontal page scrolling");
            Check(((Canvas)window.FindName("TrendChart")).Children.Count > 20, name + ": chart rendered with daily targets and axes");
            var metricGrid = PrototypeWindow.Descendants<UniformGrid>(root).Single(grid => grid.Name == "MetricGrid");
            Check(metricGrid.Columns == 5, name + ": all five KPIs remain together");
            Check(((ListBox)window.FindName("RecentList")).Items.Count == 5, name + ": finite five-row recent activity");
            var chart = (FrameworkElement)window.FindName("TrendPanel");
            var mix = (FrameworkElement)window.FindName("DistributionPanel");
            Check(Grid.GetRow(mix) == (width == 1000 ? 1 : 0), name + ": analysis panels stack when narrow");
            Check(chart.ActualWidth >= 400 && mix.ActualWidth >= 280, name + ": readable analysis widths");
            if (name == "minimum")
            {
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2);
                Capture("minimum-middle", width, height);
            }
            scroll.ScrollToBottom();
            Capture(name + "-bottom", width, height);
            var list = (ListBox)window.FindName("RecentList");
            list.ScrollIntoView(list.Items[^1]);
            Layout(width, height);
            var finalRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(4);
            var finalBounds = finalRow?.TransformToAncestor(list).TransformBounds(new Rect(finalRow.RenderSize));
            Check(finalBounds.HasValue && finalBounds.Value.Bottom <= list.ActualHeight + 1,
                name + $": last activity row is reachable in its bounded viewport (row {finalBounds}, viewport {list.ActualHeight})");
        }
        var baseline = vm.VerifiedTotal;
        ((RadioButton)window.FindName("Period7")).IsChecked = true;
        Check(vm.PeriodIndex == 0 && vm.Trend.Count == 7 && vm.VerifiedTotal != baseline, "7-day selector changes summary and chart");
        ((RadioButton)window.FindName("Period90")).IsChecked = true;
        Check(vm.PeriodIndex == 2 && vm.Trend.Count == 90 && vm.VerifiedTotal > baseline, "90-day selector changes summary and chart");
        Check(vm.Games.Sum(g => g.Earnings) == vm.VerifiedTotal, "top game amounts reconcile to verified fixture earnings");
        Check(vm.Trend.Sum(d => d.Pending) == vm.PendingTotal && vm.VerifiedCount + vm.PendingCount == vm.VisibleSessions.Count,
            "chart and distribution reconcile to the same synthetic sessions");
        ((ScrollViewer)window.FindName("DashboardScroll")).ScrollToTop();
        Capture("period-90", 1440, 960);
        ((RadioButton)window.FindName("Period30")).IsChecked = true;
        var dailyValues = (Expander)window.FindName("DailyValues");
        dailyValues.IsExpanded = true;
        Capture("daily-values", 1440, 960);
        Check(dailyValues.IsExpanded && PrototypeWindow.Descendants<ListBox>(dailyValues).Single().Items.Count == 30,
            "daily values expose an accessible bounded list for every chart day");
        dailyValues.IsExpanded = false;
        var navigation = (ListBox)window.FindName("Navigation");
        navigation.SelectedIndex = 1;
        Check(vm.Page == NavigationPage.Sessions && !vm.ShowDashboard, "sidebar routes to the isolated Sessions explorer");
        Capture("navigation", 1440, 960);
        vm.DashboardCommand.Execute(null);
        Check(navigation.SelectedIndex == 0 && vm.IsDashboard, "Dashboard return restores active sidebar state");
        vm.GameCommand.Execute(vm.Games[0].Name);
        Check(vm.IsPreviewOpen && vm.PreviewTitle == vm.Games[0].Name, "game link opens the matching synthetic preview");
        Capture("game-preview", 1440, 960);
        vm.ClosePreviewCommand.Execute(null);
        Check(!vm.IsPreviewOpen, "game preview closes without persistence");
        foreach (var state in vm.States.Where(s => s != "Populated"))
        {
            ((ComboBox)window.FindName("StateSelector")).SelectedItem = state;
            Layout(1440, 960);
            Check(vm.State == state && vm.ShowDashboard == (state == "Stale"), state + ": correct snapshot visibility");
            Capture(state.ToLowerInvariant(), 1440, 960);
        }
        vm.RetryCommand.Execute(null);
        Check(vm.State == "Populated" && vm.ShowDashboard, "Retry restores the sample snapshot");
        Capture("default-150pct", 1440, 960, 144);
        listener.Flush();
        Check(string.IsNullOrWhiteSpace(bindingErrors.ToString()), "no WPF binding warnings or errors");
        File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        window.Close();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
