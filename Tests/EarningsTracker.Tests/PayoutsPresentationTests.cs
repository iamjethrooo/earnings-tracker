using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class PayoutsPresentationTests(WpfTestHost host)
{
    [Theory]
    [InlineData(740)]
    [InlineData(1100)]
    [InlineData(1480)]
    public Task LoadedPageExplainsUndatedExclusionAndRendersSignedTransactions(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameName = "Undated session", RecordedDate = new DateTime(2026, 6, 5),
                Status = SessionStatus.Verified, Duration = TimeSpan.FromMinutes(30), Earnings = 12.34m });
            var statuses = new[] { "Paid", "Failed", "Processing", "Pending", "Verified" };
            for (var i = 0; i < statuses.Length; i++)
                db.Payouts.Add(new Payout { Date = new DateTime(2026, 6, 6), Type = i < 2 ? "Payment" : i == 2 ? "Bonus" : "Earnings correction",
                    Description = "A long transaction description with source detail that should wrap within the description column",
                    Status = statuses[i], Amount = i == 0 ? -123456.78m : i == 2 ? 25.50m : -2.50m,
                    ReconciliationState = i == 3 ? "NeedsReview" : "Separate" });
            db.SaveChanges();
        }
        var view = new PayoutsView(database.Options) { FontFamily = Font() };
        await view.RefreshHistory();
        await Layout(view, width, 680);
        Assert.Contains("1 verified session", ((TextBlock)view.FindName("UndatedSessionsNotice")).Text);
        Assert.Equal(Visibility.Visible, ((TextBlock)view.FindName("UndatedSessionsNotice")).Visibility);
        Assert.Equal("$35.34", ((TextBlock)view.FindName("TotalEarnedValue")).Text);
        Assert.Equal("$0.00", ((TextBlock)view.FindName("AmountOwedValue")).Text);
        var list = (ListView)view.FindName("PayoutList");
        list.SelectedIndex = 0;
        await Layout(view, width, 680);
        Assert.True(((Button)view.FindName("EditButton")).IsEnabled);
        Assert.Contains(view.History, row => row.AmountText == "-$123,456.78");
        Assert.Contains(view.History, row => row.Type == "Bonus" && row.AmountText == "+$25.50");
        Assert.True(list.ActualHeight > 200);
        var amountHeader = Descendants<GridViewColumnHeader>(list).Single(x => Equals(x.Content, "AMOUNT"));
        Assert.Equal(HorizontalAlignment.Right, Descendants<ContentPresenter>(amountHeader).First().HorizontalAlignment);
        Save(view, $"payments-{width}");
        // A posted date removes the warning on the next complete snapshot.
        using (var db = database.CreateContext())
        {
            db.Sessions.Single().PostedDate = new DateTime(2026, 6, 7);
            db.SaveChanges();
        }
        await view.RefreshHistory();
        Assert.Equal(Visibility.Collapsed, ((TextBlock)view.FindName("UndatedSessionsNotice")).Visibility);
        Assert.Equal("$12.34", ((TextBlock)view.FindName("AmountOwedValue")).Text);
    });

    [Fact]
    public Task LargeHistoryRealizesBoundedRowsAtBothEnds() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var view = new PayoutsView(database.Options);
        ((Border)view.FindName("HistoryPanel")).Visibility = Visibility.Visible;
        ((Grid)view.FindName("TotalsPanel")).Visibility = Visibility.Visible;
        ((BatchObservableCollection<PayoutHistoryItem>)view.History).ReplaceAll(Enumerable.Range(1, 10000).Select(i =>
            PayoutHistoryItem.FromPayout(new Payout { Id = i, Type = "Payment", Status = "Paid", Amount = -i, Description = $"Payment {i}" })));
        await Layout(view, 1100, 680);
        var list = (ListView)view.FindName("PayoutList");
        Assert.InRange(Descendants<ListViewItem>(list).Count(), 1, 100);
        Descendants<ScrollViewer>(list).First().ScrollToEnd();
        await Layout(view, 1100, 680);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(9999));
        Assert.InRange(Descendants<ListViewItem>(list).Count(), 1, 100);
    });

    [Fact]
    public Task ImportWarningsRemainVisibleAndSkippingDoesNotBypassConflicts() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Payouts.AddRange(new Payout { Type = "Payment", Status = "Paid", Description = "One", SourceId = "DUP", Amount = -1m },
                new Payout { Type = "Payment", Status = "Paid", Description = "Two", SourceId = "DUP", Amount = -2m });
            db.SaveChanges();
        }
        using var view = new ImportPayoutsView(database.Options) { FontFamily = Font() };
        ((TextBox)view.FindName("ImportTextBox")).Text = "|Posted|Recorded|Type|Description|Status|Amount|Source ID|\n"
            + "|6/5/2026|—|Payment|One|Paid|-$1.00|DUP|\n|6/5/2026|—|Payment|New|Paid|-$3.00|NEW|";
        await Preview(view);
        var confirm = (Button)view.FindName("ConfirmImportButton");
        var skip = (CheckBox)view.FindName("SkipAmbiguousOption");
        Assert.False(confirm.IsEnabled);
        Assert.Contains("Ambiguous rows", ((TextBlock)view.FindName("ImportGuidance")).Text);
        var skipped = Sections(view).Single(x => ((TextBlock)x.Header).Text.StartsWith("Skipped"));
        Assert.True(skipped.IsExpanded);
        Assert.Contains("Ambiguous payout", Assert.IsType<ListBox>(skipped.Content).Items[0].ToString());
        skip.IsChecked = true;
        Assert.True(confirm.IsEnabled);
        await Layout(view, 600, 540);
        Save(view, "import-ambiguity");
        ((Button)view.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBox)view.FindName("ImportTextBox")).Text = "|6/5/2026|—|Payment|Conflict|Paid|-$10.00|\n|6/5/2026|—|Payment|Conflict|Paid|-$12.00|";
        await Preview(view);
        skip.IsChecked = true;
        Assert.False(confirm.IsEnabled);
        Assert.Contains("Conflicting rows block", ((TextBlock)view.FindName("ImportGuidance")).Text);
        using var read = database.CreateContext();
        Assert.Equal(2, read.Payouts.Count());
        Assert.Empty(read.PayoutChanges);
    });

    [Fact]
    public Task LargeImportPreviewRecyclesDetailsAndKeepsLastRowReachable() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using var view = new ImportPayoutsView(database.Options) { FontFamily = Font() };
        ((TextBox)view.FindName("ImportTextBox")).Text = string.Join("\n", Enumerable.Range(1, 1000)
            .Select(i => $"|6/5/2026|—|Payment|Payment {i}|Paid|-$10.00|"));
        await Preview(view);
        var added = Sections(view).Single(x => ((TextBlock)x.Header).Text.StartsWith("Added"));
        added.IsExpanded = true;
        await Layout(view, 600, 680);
        var list = Assert.IsType<ListBox>(added.Content);
        Assert.Equal(1000, list.Items.Count);
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        Descendants<ScrollViewer>(list).First().ScrollToEnd();
        await Layout(view, 600, 680);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(999));
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        Assert.True(((Button)view.FindName("ConfirmImportButton")).IsEnabled);
        Save(view, "import-large-preview");
        var editor = new PayoutEditView(new Payout { Type = "Earnings correction", Status = "Verified", Amount = -2.50m }) { FontFamily = Font() };
        await Layout(editor, 600, 850);
        Save(editor, "edit-reconciliation");
        using var read = database.CreateContext();
        Assert.Empty(read.Payouts);
        Assert.Empty(read.PayoutChanges);
    });

    [Theory]
    [InlineData("Paid", "VerifiedTextColor")]
    [InlineData("Failed", "RejectedTextColor")]
    [InlineData("Processing", "PendingTextColor")]
    [InlineData("Pending", "PendingTextColor")]
    [InlineData("Legacy", "NotUploadedTextColor")]
    public Task StatusPaletteRetainsTextAndSignedAmount(string status, string resource) => host.RunAsync(() =>
    {
        var item = PayoutHistoryItem.FromPayout(new Payout { Type = "Other", Status = status, Amount = -2.50m });
        Assert.Equal(status, item.Status);
        Assert.Equal("-$2.50", item.AmountText);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource(resource)).Color, ((SolidColorBrush)item.StatusForeground).Color);
        Assert.Equal(PresentationPalette.NeutralText, ((SolidColorBrush)item.TypeForeground).Color);
        Assert.True(item.StatusForeground.IsFrozen);
        return Task.CompletedTask;
    });

    private static IEnumerable<Expander> Sections(ImportPayoutsView view) => ((StackPanel)view.FindName("ImportSummarySections")).Children.OfType<Expander>();
    private static async Task Preview(ImportPayoutsView view)
    {
        ((Button)view.FindName("ParseImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.OperationCompletion;
    }
    private static FontFamily Font() => (FontFamily)Application.Current.FindResource("AppFont");
    private static async Task Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
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
    private static void Save(FrameworkElement element, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "PayoutsVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
