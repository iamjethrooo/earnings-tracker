using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Controls;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class ModalOverviewTests(WpfTestHost host)
{
    [Theory]
    [InlineData(1488, 1058, 1)]
    [InlineData(1000, 600, 1)]
    [InlineData(1000, 720, 1.5)]
    public Task ModalLayoutsKeepActionsVisibleAndReviewListsBounded(int width, int height, double scale) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        List<Game> games;
        List<GameMetadata> metadata;
        using (var db = database.CreateContext())
        {
            games = new[] { "PlateUp!", "Wreckreation", "MAVRIX", "Predecessor" }
                .Select(name => new Game { Name = name, RatePerHour = 2, MinutesCap = 3000 }).ToList();
            db.Games.AddRange(games); db.SaveChanges(); CatalogService.ImportBundled(db);
            metadata = db.GameMetadata.ToList();
        }
        var window = new MainWindow(page => page switch
        {
            NavigationPage.Sessions => new SessionsView(database.Options),
            NavigationPage.Payments => new PayoutsView(database.Options),
            _ => new UserControl()
        }) { Width = width, Height = height, Left = -10000, Top = -10000, ShowInTaskbar = false };
        var modal = (Modal)window.FindName("MainModal");
        var suffix = $"-{width}x{height}-{scale:0.0}";
        async Task Settle()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }
        void AssertFooter(UserControl view, string action)
        {
            var button = Descendants<Button>(view).Single(item => Equals(item.Content, action));
            var panel = (Border)modal.FindName("DialogPanel");
            var bounds = Bounds(button, panel);
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= panel.ActualHeight + 1,
                $"{view.GetType().Name}/{action}: {bounds}, panel {panel.RenderSize}");
            Assert.True(bounds.Left >= 0 && bounds.Right <= panel.ActualWidth + 1);
            Assert.True(panel.ActualHeight <= window.ActualHeight - 40 + 1);
            Assert.Equal(0, ((ScrollViewer)modal.FindName("DialogScroll")).ScrollableWidth);
        }
        try
        {
            window.Show(); window.NavigateTo(NavigationPage.Sessions);
            await ((SessionsView)((ContentControl)window.FindName("MainContent")).Content).RefreshSessionsAsync();
            var input = new ImportSessionsView(database.Options);
            modal.Open("Import sessions", input);
            ((TextBox)input.FindName("ImportTextBox")).Text = "PlateUp!\nPlateUp! · Nov 21, 2026 3:13 AM · 24m · File deleted\n+$0.61\nVerified\n\nWreckreation\nWreckreation · Nov 22, 2026 6:20 PM · 1h 30m\n+$3.00\nPending";
            await Settle(); AssertFooter(input, "Preview import"); Capture((FrameworkElement)window.Content, "01-import-input" + suffix, scale);
            var review = (ImportResultModal)input.FindName("ImportResultModal");
            ((Grid)input.FindName("ImportInputPanel")).Visibility = Visibility.Collapsed;
            ((Grid)input.FindName("ImportResultContainer")).Visibility = Visibility.Visible;
            var preview = SessionPreview(games);
            review.ShowPreview(preview, games, metadata);
            await Settle();
            // Covers use the same asynchronous, bundled-artwork path as session rows.
            var rows = ((ListBox)review.FindName("ReviewRows")).Items.Cast<SessionImportReviewItem>().ToList();
            foreach (var row in rows) _ = row.Artwork?.Cover;
            for (var attempt = 0; attempt < 30 && rows.Any(row => row.Session?.GameName is "MAVRIX" or "Wreckreation" && row.Artwork?.Cover == null); attempt++)
                await Task.Delay(30);
            await Settle(); AssertFooter(review, "Import Sessions");
            Assert.Equal(1000, Modal.GetContentMaxWidth(input));
            Assert.True(VirtualizingPanel.GetIsVirtualizing((ListBox)review.FindName("ReviewRows")));
            Capture((FrameworkElement)window.Content, "02-session-review" + suffix, scale);
            modal.SetHeaderVisible(false); review.ShowSuccess("Your session records have been saved.");
            await Settle(); AssertFooter(review, "Done");
            Assert.Equal(600, Modal.GetContentMaxWidth(input));
            Assert.Contains("1 recording excluded", ((TextBlock)review.FindName("ResultExclusions")).Text);
            Capture((FrameworkElement)window.Content, "05-import-result" + suffix, scale);
            review.ShowSavedWarning("Sessions were saved, but the page could not be refreshed. Close and reopen Sessions.");
            await Settle(); AssertFooter(review, "Done");
            Assert.Equal(Visibility.Collapsed, ((Button)review.FindName("ResultBackButton")).Visibility);
            Capture((FrameworkElement)window.Content, "saved-refresh-warning" + suffix, scale);
            modal.Close();

            window.NavigateTo(NavigationPage.Payments);
            await ((PayoutsView)((ContentControl)window.FindName("MainContent")).Content).RefreshCompletion;
            var payouts = new ImportPayoutsView(database.Options);
            modal.Open("Review payout import", payouts);
            var payoutPreview = new PayoutImportResult();
            payoutPreview.NewPayouts.AddRange([
                new Payout { Date = new DateTime(2026, 11, 20), Type = "Payment", Status = "Paid", Amount = -120, Description = "November payout" },
                new Payout { Date = new DateTime(2026, 11, 22), Type = "Bonus", Status = "Verified", Amount = 5, Description = "Weekly bonus" }]);
            var previousPayment = new Payout { Id = 1, Date = new DateTime(2026, 11, 18), Type = "Payment", Status = "Processing", Amount = -50, Description = "November payout" };
            payoutPreview.Updates.Add((1, previousPayment, new Payout { Date = previousPayment.Date, Type = "Payment", Status = "Paid", Amount = -50, Description = previousPayment.Description }));
            payoutPreview.Duplicates.Add(new Payout { Date = new DateTime(2026, 11, 10), Type = "Bonus", Status = "Verified", Amount = 1, Description = "Existing bonus" });
            payoutPreview.AmbiguousPayoutMatches.Add((new Payout { Date = new DateTime(2026, 11, 19), Type = "Payment", Amount = -40, Description = "Ambiguous payment" },
                [new Payout { Id = 10, Date = new DateTime(2026, 11, 19), Amount = -40, Status = "Paid" }, new Payout { Id = 11, Date = new DateTime(2026, 11, 19), Amount = -40, Status = "Processing" }]));
            typeof(ImportPayoutsView).GetMethod("ShowSummary", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(payouts, [payoutPreview, false]);
            ((Grid)payouts.FindName("ImportInputPanel")).Visibility = Visibility.Collapsed;
            ((Grid)payouts.FindName("ImportResultContainer")).Visibility = Visibility.Visible;
            ((CheckBox)payouts.FindName("SkipAmbiguousOption")).Visibility = Visibility.Visible;
            ((Button)payouts.FindName("ConfirmImportButton")).IsEnabled = false;
            await Settle(); AssertFooter(payouts, "Import");
            Assert.Contains(Descendants<TextBlock>((StackPanel)payouts.FindName("ImportSummarySections")), text => text.Text == "-$120.00");
            Capture((FrameworkElement)window.Content, "03-payout-review" + suffix, scale);
            modal.Close();
            var editor = new PayoutEditView(new Payout { Date = new DateTime(2026, 11, 23), Type = "Earnings correction", Status = "Verified", Amount = -2.50m,
                Description = "November earnings correction", ReconciliationState = "Separate" }, database.Options);
            modal.Open("Edit payout", editor);
            ((TextBox)editor.FindName("Reason")).Text = "Correcting a duplicate bonus entry";
            await Settle(); AssertFooter(editor, "Save payout"); Capture((FrameworkElement)window.Content, "04-payout-editor" + suffix, scale);
            modal.Close();

            using var read = database.CreateContext();
            Assert.Empty(read.Sessions); Assert.Empty(read.SessionChanges); Assert.Empty(read.Payouts); Assert.Empty(read.PayoutChanges);
        }
        finally { modal.Close(); window.Close(); }

        var small = width == 1000 && height == 600;
        var roster = new CatalogRosterWindow(new([
            new("w", "Wreckreation", new(2, 3000)), new("m", "MAVRIX", new(2, 3000)), new("p", "Predecessor · $1.50 rate", new(1.5m, 3000))], ["PlateUp!"]));
        var installations = new LocalInstallationsWindow(new(true, [
            new("PlateUp!", LocalGameInstallations.SteamSource, true, 1599600),
            new("Predecessor", LocalGameInstallations.SteamSource, true, 961200),
            new("Wreckreation", LocalGameInstallations.FolderSource, true),
            new("MAVRIX", LocalGameInstallations.WindowsSource, true),
            new("Example Utility", LocalGameInstallations.WindowsSource, false)], []) { IncompleteSources = [LocalGameInstallations.FolderSource] });
        var history = new ReleaseHistoryWindow(null, ReleaseHistory.Load);
        foreach (var (utility, name) in new[] { ((Window)roster, "06-catalog-review"), (installations, "07-local-installations"), (history, "08-release-history") })
        {
            utility.Width = small ? 420 : utility.Width;
            utility.Height = small ? 400 : utility.Height;
            utility.Left = -10000; utility.Top = -10000; utility.ShowInTaskbar = false;
            try
            {
                utility.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); utility.UpdateLayout();
                var close = Descendants<Button>(utility).Single(button => button.IsCancel);
                Assert.True(Bounds(close, utility).Bottom <= utility.ActualHeight);
                Assert.True(Bounds(close, utility).Right <= utility.ActualWidth);
                var dismiss = Descendants<Button>(utility).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == "Close dialog");
                Assert.True(dismiss.IsVisible);
                Assert.True(Bounds(dismiss, utility).Right <= utility.ActualWidth,
                    $"{name}: close control {Bounds(dismiss, utility)}, window {utility.RenderSize}");
                var list = Descendants<ListBox>(utility).Single();
                Assert.True(list.ActualHeight > 0);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Capture(utility, name + suffix, scale);
            }
            finally { utility.Close(); }
        }
    });

    [Fact]
    public Task PreviewKeepsExactSourceFieldsAndRecyclesLargeLists() => host.RunAsync(async () =>
    {
        var games = new List<Game> { new() { Id = 1, Name = "Synthetic game", RatePerHour = 2 } };
        var preview = SessionPreview(games);
        preview.NewSessions[0].Earnings = 0.123456789m;
        for (var index = 0; index < 1000; index++) preview.SkippedRows.Add($"Repeated source row {index}");
        var review = new ImportResultModal(); review.ShowPreview(preview, games);
        var modal = new Modal(); Modal.SetOwnsScrolling(review, true); modal.Open("Review session import", review);
        var window = new Window { Content = modal, Width = 1000, Height = 600, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var list = (ListBox)review.FindName("ReviewRows");
            var item = (SessionImportReviewItem)list.Items[0];
            Assert.Equal("$0.123456789", item.AmountText);
            Assert.Contains("$0.123456789", item.Details);
            Assert.True(list.ActualHeight <= 360);
            Assert.True(Descendants<ListBoxItem>(list).Count() < 1000);
            var selector = (ComboBox)review.FindName("NewSessionsPayout");
            selector.SelectedIndex = 1;
            Assert.All(preview.NewSessions, session => Assert.Equal(9, session.AssignedPayoutId));
            Assert.Contains("Before:", ((SessionImportReviewItem)list.Items[3]).Before);
            Assert.Equal("Conflict · excluded", ((SessionImportReviewItem)list.Items[4]).Change);
        }
        finally { modal.Close(); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PayoutReviewRequiresExplicitAmbiguityChoiceAndCannotSkipConflicts(bool conflicting) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var seed = database.CreateContext())
        {
            seed.Payouts.Add(new Payout { Date = new DateTime(2026, 11, 5), Type = "Payment", Description = "Batch payout", Status = "Paid", Amount = -10 });
            seed.SaveChanges();
        }
        using var view = new ImportPayoutsView(database.Options);
        var modal = new Modal(); modal.Open("Import payouts", view);
        var window = new Window { Content = modal, Width = 1000, Height = 600, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            var text = "|11/12/2026|—|Payment|Batch payout|Paid|-$12.00|\n|11/22/2026|—|Bonus|Weekly bonus|Verified|+$5.00|";
            if (conflicting) text += "\n|11/22/2026|—|Bonus|Weekly bonus|Verified|+$6.00|";
            ((TextBox)view.FindName("ImportTextBox")).Text = text;
            ((Button)view.FindName("ParseImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.OperationCompletion;
            var skip = (CheckBox)view.FindName("SkipAmbiguousOption");
            var confirm = (Button)view.FindName("ConfirmImportButton");
            Assert.Equal(Visibility.Visible, skip.Visibility);
            Assert.False(confirm.IsEnabled);
            skip.IsChecked = true;
            Assert.Equal(!conflicting, confirm.IsEnabled);
            skip.IsChecked = false;
            Assert.False(confirm.IsEnabled);
            var sections = (StackPanel)view.FindName("ImportSummarySections");
            Assert.True(sections.Children.OfType<Expander>().Single(item => ((TextBlock)item.Header).Text.StartsWith("Skipped")).IsExpanded);
            using var read = database.CreateContext();
            Assert.Equal(-10, Assert.Single(read.Payouts).Amount);
            Assert.Empty(read.PayoutChanges);
        }
        finally { modal.Close(); window.Close(); }
    });

    [Fact]
    public Task CancelActionsRespectBusyGuardAndRadioSelectionRemainsExplicit() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var modal = new Modal();
        var window = new Window { Content = modal, Width = 1000, Height = 700, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            foreach (var view in new UserControl[] { new ImportSessionsView(database.Options), new AddSessionView(new Game { Name = "Synthetic game" }, database.Options), new ImportPayoutsView(database.Options) })
            {
                modal.Open("Import", view); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var cancel = Descendants<Button>(view).Single(button => Equals(button.Content, "Cancel"));
                modal.IsBusy = true;
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(view, modal.ModalContent);
                modal.IsBusy = false;
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(modal.ModalContent);
            }
            using var editor = new PayoutEditView(new Payout { Type = "Earnings correction", Status = "Verified", ReconciliationState = "NeedsReview" }, database.Options);
            modal.Open("Edit payout", editor); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var included = (RadioButton)editor.FindName("IncludedRadio");
            var separate = (RadioButton)editor.FindName("SeparateRadio");
            Assert.False(included.IsChecked); Assert.False(separate.IsChecked);
            included.IsChecked = true; separate.IsChecked = true;
            Assert.False(included.IsChecked); Assert.True(separate.IsChecked);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)separate.Template.FindName("Dot", separate)).Visibility);
            Assert.True(separate.MinHeight >= 28);
            using var read = database.CreateContext(); Assert.Empty(read.Payouts); Assert.Empty(read.PayoutChanges);
        }
        finally { modal.IsBusy = false; modal.Close(); window.Close(); }
    });

    private static ImportResult SessionPreview(IReadOnlyList<Game> games)
    {
        Session Recording(int game, decimal earnings, int minutes, SessionStatus status) => new()
        {
            GameId = games[Math.Min(game, games.Count - 1)].Id, GameName = games[Math.Min(game, games.Count - 1)].Name,
            Date = new DateTime(2026, 11, 21, 15, 13, 0).AddDays(game % 2), RecordedDate = new DateTime(2026, 11, 21, 15, 13, 0).AddDays(game % 2),
            Duration = TimeSpan.FromMinutes(minutes), Earnings = earnings, Status = status
        };
        var result = new ImportResult { PayoutOptions = [new(true, null, "Current payout"), new(true, 9, "Nov 20, 2026 · Payment #9")] };
        result.NewSessions.AddRange([Recording(0, 0.61m, 24, SessionStatus.Verified), Recording(1, 3, 90, SessionStatus.Pending), Recording(2, 1.5m, 45, SessionStatus.Verified)]);
        var updated = Recording(3, 2, 60, SessionStatus.Verified);
        result.Updates.Add((1, updated));
        result.ExpectedSessionStates.Add(1, SessionService.CaptureEditState(Recording(3, 1.5m, 45, SessionStatus.Pending)));
        result.AmbiguousSessions.Add(Recording(1, 3, 90, SessionStatus.Pending)); result.SkippedRows.Add("PlateUp! · Already present");
        return result;
    }

    private static Rect Bounds(FrameworkElement child, Visual ancestor) => child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Capture(FrameworkElement element, string name, double scale)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ModalOverviewVisuals"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); png.Save(output);
    }
}
