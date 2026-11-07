using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Controls;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class PayoutAssignmentUiTests(WpfTestHost host)
{
    [Theory]
    [InlineData(420)]
    [InlineData(620)]
    public Task PaymentEditorReviewsCoverageBeforeSavingAndLeavesUncheckedSessionsOpen(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([PayoutAssignmentTests.Recording(), PayoutAssignmentTests.Recording("Left open", 5m)]));
        using var view = new PayoutEditView(null, database.Options);
        ((DatePicker)view.FindName("PayoutDate")).SelectedDate = new DateTime(2026, 11, 5);
        ((TextBox)view.FindName("Description")).Text = "Synthetic payment";
        ((TextBox)view.FindName("Amount")).Text = "-10.00";
        var save = (Button)view.FindName("SaveButton");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.OperationCompletion;
        using (var read = database.CreateContext()) Assert.Empty(read.Payouts);
        var coverage = Assert.IsType<PayoutCoverageView>(((ContentControl)view.FindName("CoveragePanel")).Content);
        var preview = Assert.IsType<PayoutCoveragePreview>(coverage.DataContext);
        Assert.Equal(2, preview.Sessions.Count);
        var scroll = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        await Layout(scroll, width, 650);
        scroll.ScrollToEnd(); await Layout(scroll, width, 650);
        var choices = Descendants<CheckBox>(coverage).ToList();
        Assert.Equal(2, choices.Count);
        choices.Single(c => ((PayoutSessionChoice)c.DataContext).Earnings == 5m).IsChecked = false;
        Assert.Contains("1 selected", preview.Summary);
        Assert.Contains("Matches payment", preview.Summary);
        await Layout(scroll, width, 650);
        Assert.All(choices, c => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))));
        Assert.True(save.IsEnabled);
        var bounds = save.TransformToAncestor(scroll).TransformBounds(new Rect(save.RenderSize));
        Assert.InRange(bounds.Bottom, 1, 650);
        Capture(scroll, $"payment-coverage-{width}");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await view.OperationCompletion;
        using var saved = database.CreateContext();
        Assert.Single(saved.Payouts);
        Assert.Equal(5m, PayoutAccounting.Calculate(saved.Sessions.ToList(), saved.Payouts.ToList()).Balance);
    });

    [Theory]
    [InlineData(820)]
    [InlineData(1212)]
    public Task SessionAssignmentSelectorSurvivesRefreshAndSavesWithoutChangingDates(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(PayoutAssignmentTests.Payment(), options: database.Options);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([PayoutAssignmentTests.Recording()]));
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        var model = (SessionsViewModel)view.DataContext;
        var item = Assert.Single(model.Sessions);
        model.SelectedSession = item;
        item.BeginEdit();
        await Layout(view, width, 800);
        var selector = Descendants<ComboBox>(view).Single(c => AutomationProperties.GetName(c) == "Payout assignment");
        Assert.DoesNotContain(item.PayoutOptions, p => !p.IsExplicit);
        selector.SelectedItem = item.PayoutOptions.Single(p => p.PayoutId == payment.Id);
        Assert.Equal(payment.Id, item.DraftPayout!.PayoutId);
        await view.RefreshSessionsAsync(); await Layout(view, width, 800);
        Assert.Equal(payment.Id, item.DraftPayout!.PayoutId);
        Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
        ((ScrollViewer)view.FindName("DetailsScroll")).ScrollToBottom(); await Layout(view, width, 800);
        Capture(view, $"session-assignment-editor-{width}");
        if (width == 1212) Capture(view, "session-assignment-editor-144dpi", 1.5);
        Assert.Equal(item.Session.PostedDate, edited.PostedDate);
        Assert.Equal(item.Session.RecordedDate, edited.RecordedDate);
        SessionService.Update(item.Session.Id, edited, database.Options, item.EditState);
        item.CancelEdit(); await view.RefreshSessionsAsync(); await Layout(view, width, 800);
        Assert.Contains("$10.00", item.PayoutAssignmentText);
        Capture(view, $"session-assignment-{width}");
    });

    [Fact]
    public Task PaymentsDashboardAndStatisticsAgreeAfterSameDayReassignment() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(PayoutAssignmentTests.Payment(), options: database.Options);
        var importer = new SessionImporter(database.Options);
        var current = PayoutAssignmentTests.Recording("Current", 7m);
        var settled = PayoutAssignmentTests.Recording("Settled", 10m);
        importer.ImportSessions(importer.PreviewImport([current, settled]));
        settled.AssignedPayoutId = payment.Id;
        SessionService.Update(settled.Id, settled, database.Options);
        var payments = new PayoutsView(database.Options); await payments.RefreshHistory();
        Assert.Equal("$7.00", ((TextBlock)payments.FindName("AmountOwedValue")).Text);
        var day = new DateTime(2026, 11, 6);
        var dashboard = new DashboardView(database.Options); await dashboard.LoadDashboardData(asOfDate: day);
        var periods = (ComboBox)dashboard.FindName("PeriodSelector");
        periods.SelectedItem = periods.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "Payout"));
        var statistics = new StatisticsView(database.Options); await statistics.LoadStatistics(asOfDate: day);
        var ranges = (ComboBox)statistics.FindName("EarningsRangeComboBox");
        ranges.SelectedItem = ranges.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, "Payout"));
        Assert.Equal(7m, dashboard.Balance);
        Assert.Equal(7m, dashboard.PeriodEarned);
        Assert.Equal(7m, statistics.TrendPeriods.Sum(p => p.TotalEarnings));
        Assert.Equal(17m, dashboard.TotalEarned);
    });

    [Fact]
    public Task HistoricalSessionImportOffersPaymentChoiceAndPreservesExistingAssignments() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var payment = PayoutService.Create(PayoutAssignmentTests.Payment(), options: database.Options);
        var importer = new SessionImporter(database.Options);
        var preview = importer.PreviewImport([PayoutAssignmentTests.Recording()]);
        var view = new ImportResultModal(); view.ShowPreview(preview);
        var selector = (ComboBox)view.FindName("NewSessionsPayout");
        Assert.Null(Assert.IsType<SessionPayoutOption>(selector.SelectedItem).PayoutId);
        selector.SelectedItem = preview.PayoutOptions.Single(p => p.PayoutId == payment.Id);
        importer.ImportSessions(preview);
        using var read = database.CreateContext();
        Assert.Equal(payment.Id, read.Sessions.Single().AssignedPayoutId);
        Assert.Equal(0m, PayoutAccounting.Calculate(read.Sessions.ToList(), read.Payouts.ToList()).Balance);
        return Task.CompletedTask;
    });

    private static async Task Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); element.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Capture(FrameworkElement element, string name, double scale = 1)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "PayoutAssignmentVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}
