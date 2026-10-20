using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;

namespace EarningsTracker.Prototypes.Sessions;

internal static class SessionExplorerVerification
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        using var diagnostics = new StringWriter();
        using var listener = new TextWriterTraceListener(diagnostics);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var window = new PrototypeWindow();
        window.ViewModel.Page = NavigationPage.Sessions;
        var root = (FrameworkElement)window.Content;
        var view = (SessionExplorerView)window.FindName("SessionsExplorer");
        var vm = view.ViewModel;
        var table = (ListView)view.FindName("SessionTable");
        var width = 1440;
        var height = 960;
        void Check(bool result, string label)
        {
            if (!result) throw new InvalidOperationException(label);
            checks.Add("PASS " + label);
        }
        void Layout()
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout(); Pump(); root.UpdateLayout();
        }
        void Capture(string name, double dpi = 96)
        {
            Layout();
            var image = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(file);
        }
        void Finish(Task operation)
        {
            var timeout = Stopwatch.StartNew();
            while (!operation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(5)) Pump();
            Check(operation.IsCompleted, "sample save completes without blocking the dispatcher");
            operation.GetAwaiter().GetResult(); Layout();
        }
        bool InDetailsViewport(FrameworkElement element)
        {
            var scroll = (ScrollViewer)view.FindName("DetailsScroll");
            var bounds = element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
            return element.Visibility == Visibility.Visible && element.ActualHeight > 0 && bounds.Top >= -1 && bounds.Bottom <= scroll.ActualHeight + 1;
        }
        void CheckStatusLabels(string state)
        {
            var labels = PrototypeWindow.Descendants<TextBlock>(table).Where(text => vm.EditStatuses.Contains(text.Text)).ToArray();
            Check(labels.Length > 0, state + ": verification labels are realized");
            Check(labels.All(text =>
            {
                var unconstrained = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
                unconstrained.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return text.ActualWidth + 1 >= unconstrained.DesiredSize.Width;
            }), state + ": full verification text fits after recovery and row recycling");
        }
        try
        {
            foreach (var size in new[] { ("minimum", 1000, 600), ("compact", 1200, 720), ("default", 1440, 960), ("wide", 1800, 1000) })
            {
                (var name, width, height) = size;
                Capture(name + "-selected");
                var realized = PrototypeWindow.Descendants<ListViewItem>(table).Count();
                Check(table.ActualHeight >= 180, name + ": table has a bounded usable viewport");
                Check(realized is > 0 and < 100 && vm.VisibleCount > 500, name + $": recycling realizes {realized} rows for {vm.VisibleCount} matches");
                var panel = (Border)view.FindName("DetailsPanel");
                Check(panel.ActualWidth is >= 300 and <= 360, name + ": readable contextual panel width");
                Check(Grid.GetColumn(panel) == (width < 1380 ? 0 : 2), name + ": drawer or adjacent panel follows available width");
                Check(panel.TransformToAncestor(root).TransformBounds(new Rect(panel.RenderSize)).Right <= width,
                    name + ": details stay within the window");
                Check(((Border)view.FindName("TableHost")).IsEnabled == (width >= 1380), name + ": drawer removes the covered table from interaction");
                if (width < 1380)
                {
                    vm.CloseDetailsCommand.Execute(null); Capture(name + "-table");
                    Check(table.IsEnabled && vm.SelectedSession == null, name + ": closing the drawer restores table interaction");
                    vm.SelectedSession = vm.Rows.Cast<ExplorerSession>().First();
                }
            }
            width = 1440; height = 960; Layout();
            var first = vm.SelectedSession!;
            vm.CloseDetailsCommand.Execute(null);
            Check(vm.SelectedSession == null, "details can close without changing records");
            vm.ClearFiltersCommand.Execute(null);
            Check(vm.VisibleCount == 1500, "all synthetic dates expose 1,500 rows");
            var last = vm.Rows.Cast<ExplorerSession>().Last();
            table.ScrollIntoView(last); Layout();
            Check(table.ItemContainerGenerator.ContainerFromItem(last) is ListViewItem,
                "last synthetic record is reachable by native virtualized scrolling");
            Check(PrototypeWindow.Descendants<ListViewItem>(table).Count() < 100, "scrolling to the end keeps virtualization active");
            Capture("last-record");
            vm.Search = "SAMPLE-20481";
            Check(vm.VisibleCount == 1, "source ID search finds the expected record");
            vm.Search = "Orbital"; vm.StatusFilter = "Pending"; vm.DateFilter = "Last 7 days";
            Check(vm.VisibleCount > 0 && vm.Rows.Cast<ExplorerSession>().All(r => r.Game == "Orbital Drift" && r.Status == "Pending" && r.Recorded >= DashboardViewModel.AsOf.Date.AddDays(-6)),
                "search, status and recording-date filters compose");
            Capture("filtered");
            vm.GameFilter = "Neon Circuit";
            Check(vm.VisibleCount == 0 && vm.StateCanClear, "contradictory game filter shows filtered-empty guidance");
            Capture("filtered-empty");
            vm.ClearFilters(); vm.Sort = "Earnings high to low";
            var amounts = vm.Rows.Cast<ExplorerSession>().Select(r => r.Earnings).ToArray();
            Check(amounts.SequenceEqual(amounts.OrderByDescending(v => v)), "earnings sorting uses numeric values");
            vm.Sort = "Newest first"; vm.DateFilter = "Last 30 days"; vm.SelectedSession = first;
            table.ScrollIntoView(first); Layout();
            vm.GameCommand.Execute("Neon Circuit"); Layout();
            Check(window.ViewModel.IsPreviewOpen && vm.SelectedSession == first, "game navigation is separate from session selection");
            Check((string)((Button)window.FindName("ClosePreviewButton")).Content == "Back to session" &&
                !window.ViewModel.PreviewDescription.Contains("browsing and real record integration remain deferred"), "Sessions preview names its actual return destination");
            Capture("game-navigation");
            window.ViewModel.ClosePreviewCommand.Execute(null);
            vm.EditCommand.Execute(null);
            Check(vm.IsEditing && !vm.HasUnsavedChanges, "Edit begins a clean separate draft");
            Capture("editing");
            vm.DraftMinutes = "-8"; vm.DraftNote = "Checking prototype validation";
            var originalAmount = first.Earnings;
            Finish(vm.SaveAsync());
            Check(vm.Validation.Contains("duration") && vm.SelectedSession!.Earnings == originalAmount, "invalid duration retains the original record");
            Check(vm.DurationValidation == vm.Validation && vm.EarningsValidation.Length == 0, "duration error belongs to duration only");
            vm.DraftNote = "Unrelated reason edit";
            Check(vm.DurationValidation.Length > 0, "unrelated input does not hide the duration error");
            Capture("validation");
            width = 1000; height = 600; Layout();
            var detailsScroll = (ScrollViewer)view.FindName("DetailsScroll");
            detailsScroll.ScrollToBottom(); Layout();
            Finish(vm.SaveAsync());
            Check(InDetailsViewport((TextBox)view.FindName("MinutesEditor")) && InDetailsViewport((TextBlock)view.FindName("DurationError")),
                "narrow failed save reveals both duration input and adjacent error from the bottom of the drawer");
            Capture("minimum-validation-duration");
            detailsScroll.ScrollToBottom(); Layout(); Finish(vm.SaveAsync());
            Check(InDetailsViewport((TextBlock)view.FindName("DurationError")), "repeated failed save reveals the unchanged duration error");
            vm.DraftMinutes = "92"; vm.DraftEarnings = "-2";
            Finish(vm.SaveAsync());
            Check(vm.Validation.Contains("nonnegative"), "negative earnings are rejected without normalization");
            Check(vm.DurationValidation.Length == 0 && vm.EarningsValidation == vm.Validation && InDetailsViewport((TextBlock)view.FindName("EarningsError")),
                "narrow earnings validation is adjacent and scrolled into view");
            Capture("minimum-validation-earnings");
            vm.DraftEarnings = "12.3456"; vm.DraftStatus = "Unsupported";
            Finish(vm.SaveAsync());
            Check(vm.VerificationValidation == vm.Validation && InDetailsViewport((TextBlock)view.FindName("VerificationError")), "unsupported verification error is adjacent and visible in the narrow drawer");
            vm.DraftStatus = "Pending"; vm.DraftNote = "";
            Finish(vm.SaveAsync());
            Check(vm.ReasonValidation == vm.Validation && InDetailsViewport((TextBlock)view.FindName("ReasonError")), "required reason error is adjacent and visible in the narrow drawer");
            Capture("minimum-validation-reason");
            vm.DraftNote = "Checking prototype validation";
            width = 1440; height = 960; Layout();
            vm.DraftEarnings = "12.3456";
            Check(vm.HasUnsavedChanges && !vm.TryLeave(), "unsaved draft blocks leaving with explicit Save/Cancel guidance");
            window.ViewModel.Page = NavigationPage.Dashboard;
            Check(window.ViewModel.IsSessions, "shell navigation preserves an unsaved Sessions draft");
            Capture("unsaved");
            vm.CancelCommand.Execute(null);
            Check(!vm.IsEditing && vm.SelectedSession!.Earnings == originalAmount, "Cancel discards the draft without changing the row");
            vm.EditCommand.Execute(null); vm.DraftEarnings = "12.3456"; vm.DraftNote = "Synthetic source correction";
            var save = vm.SaveAsync();
            Check(vm.IsBusy && !vm.CanEditFields && !vm.CancelCommand.CanExecute(null) && !vm.TryLeave(), "busy state guards editing, Cancel and navigation");
            Capture("busy"); Finish(save);
            Check(!vm.IsEditing && vm.SelectedSession!.Earnings == 12.3456m && vm.SelectedSession.SourceValues == first.SourceValues,
                "successful sample save preserves decimal precision and original source evidence");
            vm.EditCommand.Execute(null); vm.DraftNote = "Review conflict"; vm.SaveResult = "Conflict";
            Finish(vm.SaveAsync());
            Check(vm.IsEditing && vm.Feedback.StartsWith("Conflict:") && vm.DraftNote == "Review conflict", "conflict retains the draft and blocks overwrite");
            Capture("conflict");
            width = 1000; height = 600; Capture("minimum-conflict");
            Check(InDetailsViewport((Border)view.FindName("PanelFeedback")), "narrow drawer reveals existing conflict feedback after resizing");
            detailsScroll.ScrollToBottom(); Layout(); Finish(vm.SaveAsync());
            Check(InDetailsViewport((Border)view.FindName("PanelFeedback")), "repeated conflict save reveals feedback from a scrolled narrow drawer");
            Capture("minimum-conflict-repeated");
            width = 1440; height = 960;
            vm.CancelCommand.Execute(null); vm.EditCommand.Execute(null);
            vm.SaveResult = "Saved · refresh failed"; vm.DraftNote = "Accepted sample correction";
            Finish(vm.SaveAsync());
            Check(!vm.IsEditing && vm.ShowRefreshRetry && vm.Feedback.Contains("Do not save again"), "saved-but-refresh-failed is distinguished from failed saving");
            Capture("saved-refresh-failed");
            CheckStatusLabels("saved warning");
            width = 1000; height = 600; Capture("minimum-saved-refresh-failed");
            Check(InDetailsViewport((Border)view.FindName("PanelFeedback")), "narrow saved warning and retry are revealed after resizing");
            detailsScroll.ScrollToBottom(); Layout();
            var savedRevision = vm.SelectedSession!.Revision;
            vm.RetryCommand.Execute(null);
            Layout();
            Check(!vm.ShowRefreshRetry && vm.SelectedSession.Revision == savedRevision, "refresh retry never repeats the sample write");
            Capture("minimum-refreshed-feedback");
            Check(InDetailsViewport((Border)view.FindName("PanelFeedback")), "narrow refresh recovery remains visible after retry from a scrolled drawer");
            foreach (var size in new[] { ("minimum", 1000, 600), ("default", 1440, 960), ("wide", 1800, 1000) })
            {
                (var name, width, height) = size;
                var notUploaded = vm.Rows.Cast<ExplorerSession>().First(row => row.Status == "Not uploaded");
                table.ScrollIntoView(notUploaded); Layout();
                Check(PrototypeWindow.Descendants<TextBlock>(table).Any(text => text.Text == "Not uploaded"), name + ": longest status is realized after recovery");
                CheckStatusLabels(name + " refreshed");
                Capture(name + "-refreshed-status");
            }
            width = 1440; height = 960;
            foreach (var example in vm.Examples.Where(s => s != "Populated"))
            {
                vm.Example = example; Layout();
                Check(example == "Stale" ? vm.VisibleCount > 0 && vm.IsStale : vm.VisibleCount == 0 && vm.ShowState,
                    example + ": distinct snapshot visibility and feedback");
                Capture(example.ToLowerInvariant().Replace(' ', '-'));
            }
            vm.RetryCommand.Execute(null);
            vm.ImportCommand.Execute(null); Capture("import-preview");
            Check(vm.IsImportOpen && vm.TotalCount == 1500, "import preview accesses no importer and adds no rows");
            vm.CloseImportCommand.Execute(null);
            vm.SelectedSession = vm.Rows.Cast<ExplorerSession>().First();
            Capture("default-150pct", 144);
            window.ViewModel.Page = NavigationPage.Dashboard; Layout();
            Check(window.ViewModel.PreviewReturnLabel == "Back to overview", "Dashboard retains its overview return wording");
            listener.Flush();
            File.WriteAllText(Path.Combine(directory, "bindings.txt"), diagnostics.ToString());
            Check(string.IsNullOrWhiteSpace(diagnostics.ToString()), "no WPF binding warnings or errors");
            File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            SynchronizationContext.SetSynchronizationContext(previousContext);
            // Avoid a test draft preventing disposal after a failed assertion.
            if (!vm.IsBusy) vm.CancelCommand.Execute(null);
            window.Close();
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
