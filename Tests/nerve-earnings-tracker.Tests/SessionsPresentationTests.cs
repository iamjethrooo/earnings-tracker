using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;
using nerve_earnings_tracker.Views;
using nerve_earnings_tracker.Views.Modals;

namespace nerve_earnings_tracker.Tests;

[Collection("Wpf")]
public class SessionsPresentationTests(WpfTestHost host)
{
    [Fact]
    public Task LargeSessionsListKeepsBoundedContainersAcrossSortingFilteringAndScrolling() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var view = new SessionsView(database.Options) { Width = 740, Height = 560 };
        var model = (SessionsViewModel)view.DataContext;
        // Synthetic presentation rows avoid production startup and database access.
        ((BatchObservableCollection<SessionItemViewModel>)model.Sessions).ReplaceAll(
            Enumerable.Range(1, 10000).Select(i => new SessionItemViewModel(new Session
            {
                Id = i, GameName = i % 2 == 0 ? "Alpha" : "Zulu",
                RecordedDate = new DateTime(2026, 10, 12).AddMinutes(-i), Earnings = i,
                Duration = TimeSpan.FromMinutes(i), Status = i % 2 == 0 ? SessionStatus.Verified : SessionStatus.Pending
            })));
        await Layout(view, 740, 560);
        var list = (ListBox)view.FindName("SessionsList");
        Assert.Equal(10000, list.Items.Count);
        Assert.Equal(1, ((SessionItemViewModel)list.Items[0]).Session.Id);
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        Assert.Empty(Descendants<DatePicker>(list)); // No editor controls are built for browsing rows.
        var scroll = Descendants<ScrollViewer>(list).First();
        scroll.ScrollToEnd();
        await Layout(view, 740, 560);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(9999));
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        model.SelectedSortIndex = 3;
        Assert.Equal(10000, ((SessionItemViewModel)list.Items[0]).Session.Id);
        model.SelectStatus(model.StatusFilters.Single(x => x.Status == SessionStatus.Verified));
        model.SearchText = "Alpha";
        Assert.Equal(5000, list.Items.Count);
        scroll.ScrollToEnd();
        await Layout(view, 740, 560);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(4999));
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 100);
        model.SelectedSortIndex = 1;
        Assert.Equal(10000, ((SessionItemViewModel)list.Items[0]).Session.Id);
        model.SelectedSortIndex = 4;
        Assert.Equal(10000, ((SessionItemViewModel)list.Items[0]).Session.Id);
    });

    [Theory]
    [InlineData(740)]
    [InlineData(1200)]
    public Task SessionLayoutsRenderLongNamesStatusesAndEditorWithoutClippingActions(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var view = new SessionsView(database.Options) { Width = width, Height = 680, FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        var model = (SessionsViewModel)view.DataContext;
        await view.RefreshSessionsAsync();
        await Layout(view, width, 680);
        Assert.Equal(Visibility.Visible, ((StackPanel)view.FindName("SessionsEmptyPanel")).Visibility);
        Save(view, $"sessions-empty-{width}");
        using (var db = database.CreateContext())
        {
            foreach (var status in Enum.GetValues<SessionStatus>())
                db.Sessions.Add(new Session
                {
                    GameName = "A long game title with an edition and downloadable content",
                    RecordedDate = new DateTime(2026, 10, 12, 21, 15, 0), Duration = TimeSpan.FromMinutes(125),
                    Earnings = 1234.56m, Status = status, IsForceVerified = status == SessionStatus.Verified
                });
            db.SaveChanges();
        }
        await view.RefreshSessionsAsync();
        await Layout(view, width, 680);
        var list = (ListBox)view.FindName("SessionsList");
        list.SelectedIndex = 0;
        await Layout(view, width, 680);
        foreach (var button in Descendants<Button>(list).Where(b => b.IsVisible && b.Content is string text && (text == "Edit" || text == "Delete")))
        {
            var right = button.TranslatePoint(new Point(button.ActualWidth, 0), view).X;
            Assert.InRange(right, 0, width);
            Assert.True(button.ActualWidth > 40);
        }
        Save(view, $"sessions-{width}");
        foreach (var chip in Descendants<RadioButton>(view).Where(x => x.DataContext is SessionStatusFilterItem { Status: SessionStatus.Verified }))
        {
            Assert.Same(Application.Current.FindResource("VerifiedTextColor"), chip.Foreground);
            Assert.Same(chip.Foreground, Descendants<TextBlock>(chip).Single().Foreground);
        }
        var editing = (SessionItemViewModel)list.Items[0];
        editing.BeginEdit();
        await Layout(view, width, 680);
        list.ScrollIntoView(editing);
        await Layout(view, width, 680);
        Save(view, $"sessions-editor-{width}");
        Assert.Equal(2, Descendants<DatePicker>(list).Count());
        editing.CancelEdit();
        await Layout(view, width, 680);
        Assert.Empty(Descendants<DatePicker>(list));
        ((TextBox)view.FindName("SearchSessionsBox")).Text = "no matching game";
        await Layout(view, width, 680);
        Assert.True(model.SessionsView.IsEmpty);
        Assert.Equal(Visibility.Visible, ((Button)view.FindName("ClearSessionFiltersButton")).Visibility);
        Save(view, $"sessions-filtered-empty-{width}");
        ((Button)view.FindName("ClearSessionFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, list.Items.Count);
    });

    [Fact]
    public Task ImportFeedbackUsesSemanticTokensAndOffersRecoveryOnlyBeforeSuccess() => host.RunAsync(async () =>
    {
        var summary = new ImportResultModal { Width = 600, FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        var result = new ImportResult();
        result.NewSessions.Add(new Session { Earnings = 2.50m });
        result.AmbiguousSessions.Add(new Session());
        result.InvalidSessions.Add(new Session());
        result.SkippedRows.Add("repeated");
        summary.ShowPreview(result);
        await Layout(summary, 600, 400);
        Assert.Same(Application.Current.FindResource("PendingTextColor"), ((TextBlock)summary.FindName("AmbiguousSessionsText")).Foreground);
        Assert.Same(Application.Current.FindResource("RejectedTextColor"), ((TextBlock)summary.FindName("InvalidSessionsText")).Foreground);
        Save(summary, "session-import-preview");
        summary.ShowError("Unable to parse the imported data: line 4 has an incomplete duration. Return to input to correct it.");
        await Layout(summary, 600, 400);
        var back = (Button)summary.FindName("ResultBackButton");
        Assert.Equal(Visibility.Visible, back.Visibility);
        var recovered = false;
        summary.BackClicked += (_, _) => recovered = true;
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(recovered);
        Save(summary, "session-import-error");
        summary.ShowSuccess("Successfully imported 1 session.");
        Assert.Equal(Visibility.Collapsed, back.Visibility);
        summary.ShowSavedWarning("Sessions were saved, but the display could not be updated.");
        Assert.Equal(Visibility.Collapsed, back.Visibility);
        await Layout(summary, 600, 400);
        Save(summary, "session-import-saved-warning");
        using var database = new TestDatabase();
        using var input = new ImportSessionsView(database.Options);
        await Layout(input, 600, 450);
        Save(input, "session-import-input");
    });

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
        var root = Path.Combine(AppContext.BaseDirectory, "SessionsVisuals");
        Directory.CreateDirectory(root);
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(root, name + ".png"));
        encoder.Save(file);
    }
}
