using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Automation;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.ViewModels;
using nerve_earnings_tracker.Views;
using nerve_earnings_tracker.Views.Modals;
using nerve_earnings_tracker.Controls;

namespace nerve_earnings_tracker.Tests;

[Collection("Wpf")]
public class ResponsiveLayoutTests(WpfTestHost host)
{
    private const string LongTitle = "A very long game title with an expansion, complete edition, and additional descriptive text";

    [Fact]
    public Task EmptyImportShowsInlineGuidanceAndFocusesInputWithoutSaving() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = LongTitle };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        var modal = new Modal { Visibility = Visibility.Collapsed };
        var window = TestWindow(modal);
        try
        {
            window.Show();
            foreach (var view in new UserControl[] { new AddSessionView(game, database.Options),
                new ImportSessionsView(database.Options), new ImportPayoutsView(database.Options) })
            {
                modal.Open("Import", view);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var parse = Descendants<Button>(view).Single(b => b.Content as string == "Parse Import");
                parse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var input = (TextBox)view.FindName("ImportTextBox");
                var feedback = (TextBlock)view.FindName("InputValidationMessage");
                Assert.Equal(Visibility.Visible, feedback.Visibility);
                Assert.Contains("Paste", feedback.Text);
                Assert.True(input.IsKeyboardFocusWithin);
                Assert.Equal(Visibility.Collapsed, ((Grid)view.FindName("ImportResultContainer")).Visibility);
                modal.Close();
            }
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.Payouts);
        }
        finally { modal.Close(); window.Close(); }
    });

    [Fact]
    public Task PayoutValidationKeepsDialogOpenAndFocusesInvalidField() => host.RunAsync(async () =>
    {
        var view = new PayoutEditView();
        var modal = new Modal { ModalContent = view, Title = "Edit payout" };
        var window = TestWindow(modal);
        var saved = 0;
        view.Saved += (_, _) => saved++;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var date = (DatePicker)view.FindName("PayoutDate");
            var amount = (TextBox)view.FindName("Amount");
            var save = (Button)view.FindName("SaveButton");
            var feedback = (TextBlock)view.FindName("ValidationMessage");
            date.SelectedDate = null;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Choose a date, type, and status.", feedback.Text);
            Assert.True(date.IsKeyboardFocusWithin);
            date.SelectedDate = DateTime.Today;
            amount.Text = "invalid amount";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("signed USD amount", feedback.Text);
            Assert.True(amount.IsKeyboardFocusWithin);
            amount.Text = "1.00";
            ((ComboBox)view.FindName("PayoutType")).SelectedItem = "Bonus";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("already included", feedback.Text);
            Assert.True(((RadioButton)view.FindName("IncludedRadio")).IsKeyboardFocusWithin);
            Assert.Equal(Visibility.Visible, feedback.Visibility);
            Assert.Same(view, modal.ModalContent);
            Assert.Equal(0, saved);
            Snapshot(modal, "payout-inline-validation");
        }
        finally { modal.Close(); window.Close(); }
    });

    [Fact]
    public Task OwnershipDialogCanResizeScrollAndFocusInvalidInputs() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = string.Join(" ", Enumerable.Repeat(LongTitle, 5)) });
            db.SaveChanges();
        }
        var item = Assert.Single(new GamesViewModel(database.Options).Games);
        var window = new GameOwnershipWindow(item) { ShowInTaskbar = false, Left = -10000, Top = -10000,
            WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            window.SizeToContent = SizeToContent.Manual;
            window.Width = 360; window.Height = 340;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            Assert.True(scroll.ScrollableHeight > 0);
            var cost = Descendants<TextBox>(window).Single(t => AutomationProperties.GetName(t) == "Purchase cost (optional)");
            var currency = Descendants<TextBox>(window).Single(t => AutomationProperties.GetName(t) == "Currency (three-letter code)");
            var save = Descendants<Button>(window).Single(b => b.Content as string == "Save");
            cost.Text = "invalid";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(cost.IsKeyboardFocusWithin);
            cost.Text = ""; currency.Text = "X";
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(currency.IsKeyboardFocusWithin);
            scroll.ScrollToBottom();
            window.UpdateLayout();
            Assert.True(Bounds(save, scroll).Bottom <= scroll.ActualHeight + 1);
            using var read = database.CreateContext();
            Assert.Empty(read.GameOwnership);
            Snapshot(scroll, "ownership-small");
        }
        finally { window.Close(); }
    });

    private static Window TestWindow(UIElement content) => new()
    {
        Content = content, Width = 1000, Height = 600, ShowInTaskbar = false, Left = -10000, Top = -10000
    };

    [Theory]
    [InlineData(520, 420)]
    [InlineData(1000, 568)]
    [InlineData(1200, 688)]
    public Task ModalFormsFitViewportAndLastActionRemainsReachable(double width, double height) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = new Game { Name = LongTitle };
        using (var db = database.CreateContext()) { db.Games.Add(game); db.SaveChanges(); }
        foreach (var view in new UserControl[] { new PayoutEditView(), new AddSessionView(game, database.Options),
            new ImportSessionsView(database.Options), new ImportPayoutsView(database.Options) })
        {
            var modal = new Modal { Title = LongTitle, ModalContent = view };
            try
            {
                if (view.FindName("ImportTextBox") is TextBox input)
                    input.Text = string.Join(Environment.NewLine, Enumerable.Repeat(LongTitle, 50));
                Layout(modal, width, height);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Layout(modal, width, height);
                var panel = (Border)modal.FindName("DialogPanel");
                Assert.True(panel.ActualWidth <= width - 40 + 1);
                Assert.True(panel.ActualHeight <= height - 40 + 1);
                var scroll = (ScrollViewer)modal.FindName("DialogScroll");
                Assert.Equal(0, scroll.ScrollableWidth);
                var action = Descendants<Button>(view).Single(b => b.Content as string is "Parse Import" or "Save payout");
                scroll.ScrollToBottom();
                modal.UpdateLayout();
                Assert.True(Bounds(action, scroll).Bottom <= scroll.ActualHeight + 1);
                Assert.True(Bounds(action, scroll).Top >= -1);
                Snapshot(modal, $"{view.GetType().Name}-dialog-{width}");
            }
            finally { modal.Close(); }
        }
    });

    [Theory]
    [InlineData(740)]
    [InlineData(940)]
    [InlineData(1480)]
    public Task TablesKeepReadableColumnsAndExposeOverflow(double width) => host.RunAsync(async () =>
    {
        var payouts = new PayoutsView();
        ((Border)payouts.FindName("HistoryPanel")).Visibility = Visibility.Visible;
        ((Grid)payouts.FindName("TotalsPanel")).Visibility = Visibility.Visible;
        payouts.History.Add(PayoutHistoryItem.FromPayout(new Payout { Date = DateTime.Today, Amount = -123456.78m,
            Type = "Payment", Status = "Processing", Description = LongTitle }));
        var statistics = new StatisticsView();
        ((StackPanel)statistics.FindName("TotalsPanel")).Visibility = Visibility.Visible;
        statistics.GameCapRows.Add(new GameCapItem(new Game { Name = LongTitle, MinutesCap = 500, RatePerHour = 123.45m }, 123, 123456.78m));
        foreach (var view in new UserControl[] { payouts, statistics })
        {
            Layout(view, width, 568);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Layout(view, width, 568);
            var list = (ListView)view.FindName(view is PayoutsView ? "PayoutList" : "GameCapList");
            var columns = ((GridView)list.View).Columns;
            var scroll = Descendants<ScrollViewer>(list).First();
            if (view is StatisticsView)
            {
                Assert.True(columns.Sum(column => column.Width) <= list.ActualWidth - 12.5);
                Assert.Equal(0, scroll.ScrollableWidth);
                Assert.Equal(Visibility.Collapsed, scroll.ComputedHorizontalScrollBarVisibility);
            }
            else if (width == 740)
            {
                Assert.All(columns, c => Assert.True(c.Width >= 80));
                Assert.True(columns.Last().Width >= 125);
                Assert.True(scroll.ScrollableWidth > 0);
                scroll.ScrollToRightEnd();
                list.UpdateLayout();
                Assert.True(scroll.HorizontalOffset > 0);
            }
            else
                Assert.Equal(0, scroll.ScrollableWidth, 1);
            Snapshot(view, $"{view.GetType().Name}-{width}");
        }
        var trend = (Border)statistics.FindName("TrendPanel");
        var games = (Border)statistics.FindName("GameEarningsPanel");
        var charts = (Grid)statistics.FindName("ChartsGrid");
        if (width == 740)
            Assert.True(Bounds(games, charts).Top >= Bounds(trend, charts).Bottom);
        else
            Assert.True(Bounds(games, charts).Left >= Bounds(trend, charts).Right);
    });

    [Theory]
    [InlineData(740)]
    [InlineData(940)]
    [InlineData(1480)]
    public Task GamesToolbarAndLongTitlesFitAvailableContent(double width) => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = LongTitle, IsArchived = true });
            db.SaveChanges();
        }
        var view = new GamesView(database.Options);
        Layout(view, width, 568);
        var toolbar = (WrapPanel)view.FindName("GamesToolbar");
        foreach (FrameworkElement child in toolbar.Children)
            Assert.True(Bounds(child, toolbar).Right <= toolbar.ActualWidth + 1);
        var list = (ListBox)view.FindName("GamesList");
        var card = (FrameworkElement)list.ItemTemplate.LoadContent();
        card.DataContext = Assert.Single(((GamesViewModel)view.DataContext).Games);
        Layout(card, width - 62, 160);
        var title = Descendants<TextBlock>(card).Single(t => t.Text == LongTitle);
        var archived = Descendants<TextBlock>(card).Single(t => t.Text == "Archived");
        Assert.True(title.ActualWidth > 0);
        Assert.True(Bounds(title, card).Right <= Bounds(archived, card).Left);
        Snapshot(view, $"games-{width}");
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(740)]
    [InlineData(940)]
    [InlineData(1480)]
    public Task SessionRowsKeepLongTitlesBadgesAndActionsSeparate(double width) => host.RunAsync(() =>
    {
        foreach (var view in new UserControl[] { new SessionsView(), new DashboardView() })
        {
            var list = (ListBox)view.FindName("SessionsList");
            var row = (FrameworkElement)list.ItemTemplate.LoadContent();
            row.DataContext = new SessionItemViewModel(new Session { GameName = LongTitle, IsForceVerified = true,
                Status = SessionStatus.Verified, Earnings = 123456.78m, Duration = TimeSpan.FromMinutes(5),
                Date = DateTime.Today, RecordedDate = DateTime.Today });
            Layout(row, width - 62, 120);
            var title = Descendants<TextBlock>(row).Single(t => t.Text == LongTitle);
            var amount = Descendants<TextBlock>(row).Single(t => t.Text.Contains("123,456.78") || t.Text.Contains("123456.78"));
            Assert.True(title.ActualWidth > 0);
            Assert.True(Bounds(title, row).Right <= Bounds(amount, row).Left);
            foreach (var button in Descendants<Button>(row))
                Assert.True(Bounds(button, row).Right <= row.ActualWidth + 1);
            Snapshot(row, $"{view.GetType().Name}-row-{width}");
        }
        return Task.CompletedTask;
    });

    private static Rect Bounds(FrameworkElement child, Visual parent) =>
        child.TransformToAncestor(parent).TransformBounds(new Rect(child.RenderSize));

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Snapshot(FrameworkElement element, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ResponsiveVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
