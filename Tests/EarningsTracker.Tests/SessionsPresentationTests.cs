using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class SessionsPresentationTests(WpfTestHost host)
{
    [Theory]
    [InlineData(740, 560)]
    [InlineData(1212, 900)]
    [InlineData(1572, 940)]
    public Task DetailsValidationAndSavedWarningPreserveDraftAndSaveOnce(int width, int height) => host.RunAsync(async () =>
    {
        using var bindingTrace = new BindingTrace();
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([new Session
        {
            GameName = "Synthetic editor", Date = new DateTime(2026, 10, 1),
            RecordedDate = new DateTime(2026, 10, 1), Duration = TimeSpan.FromMinutes(5),
            Earnings = 1.2345m, Status = SessionStatus.Pending
        }]));
        var view = new SessionsView(database.Options);
        var model = (SessionsViewModel)view.DataContext;
        await view.RefreshSessionsAsync();
        var item = Assert.Single(model.Sessions);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        item.DraftDurationMinutes = "-1";
        await Layout(view, width, height);
        var save = Descendants<Button>(view).Single(button => Equals(button.Content, "Save changes"));
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, width, height);
        Assert.True(item.IsEditing);
        Assert.False(item.IsSaving);
        Assert.NotNull(item.DurationError);
        var duration = Descendants<TextBox>(view).Single(box => Equals(box.Tag, nameof(item.DraftDurationMinutes)));
        Assert.Equal(item.DurationError, System.Windows.Automation.AutomationProperties.GetHelpText(duration));
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == item.DurationError && text.ActualHeight > 0);
        Save(view, $"pro47-validation-{width}");
        item.DraftEarnings = "2";
        Assert.NotNull(item.DurationError); // Unrelated edits do not erase the error.
        item.DraftDurationMinutes = "6";
        var notifications = 0;
        EventHandler failedRefresh = (_, _) => { notifications++; throw new InvalidOperationException("Synthetic refresh failure"); };
        PayoutService.DataChanged += failedRefresh;
        try { save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { PayoutService.DataChanged -= failedRefresh; }
        Assert.False(item.IsEditing);
        Assert.False(item.IsSaving);
        Assert.Equal(1, notifications);
        var feedback = (TextBlock)view.FindName("EditorFeedback");
        Assert.Contains("Do not save again", feedback.Text);
        // A queued click on the old editor cannot repeat the committed write.
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using (var db = database.CreateContext())
        {
            Assert.Equal(2m, Assert.Single(db.Sessions).Earnings);
            Assert.Single(db.SessionChanges.Where(change => change.ChangeType == "Updated"));
        }
        await Layout(view, width, height);
        Save(view, $"pro47-saved-warning-{width}");
        ((Button)view.FindName("PanelRetryButton"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await view.RefreshSessionsAsync();
        using (var db = database.CreateContext())
            Assert.Single(db.SessionChanges.Where(change => change.ChangeType == "Updated"));
    });

    [Fact]
    public Task EditorFocusReturnsToDetailsAfterCancel() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([new Session
        {
            GameName = "Synthetic focus", Date = new DateTime(2026, 10, 1),
            RecordedDate = new DateTime(2026, 10, 1), Duration = TimeSpan.FromMinutes(5), Status = SessionStatus.Pending
        }]));
        var view = new SessionsView(database.Options);
        var window = new Window { Content = view, Width = 1000, Height = 700, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await view.RefreshSessionsAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            view.SelectSession(Assert.Single(((SessionsViewModel)view.DataContext).Sessions));
            Descendants<Button>(view).Single(button => Equals(button.Content, "Edit session"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(Descendants<DatePicker>(view).First().IsKeyboardFocusWithin);
            Assert.False(((Border)view.FindName("SessionFilters")).IsEnabled);
            var item = Assert.Single(((SessionsViewModel)view.DataContext).Sessions);
            item.DraftDurationMinutes = "-1";
            Descendants<Button>(view).Single(button => Equals(button.Content, "Save changes"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Descendants<TextBox>(view).Single(box => Equals(box.Tag, nameof(item.DraftDurationMinutes))).IsKeyboardFocusWithin);
            Descendants<Button>(view).Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(item.IsEditing);
            Assert.False(view.HasActiveDraft);
            Assert.False(((Grid)view.FindName("BrowseContent")).IsEnabled); // Read drawer still contains focus.
            Assert.True(((Button)view.FindName("EditSessionButton")).IsKeyboardFocused);
            Assert.Equal(TimeSpan.FromMinutes(5), item.Session.Duration);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NavigationKeepsDraftSelectionAndRecreatesPagesAfterCancel() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var created = new List<NavigationPage>();
        var view = new SessionsView(database.Options);
        var model = (SessionsViewModel)view.DataContext;
        var item = new SessionItemViewModel(new Session { GameName = "Synthetic", Earnings = 3m });
        model.Sessions.Add(item);
        var window = new MainWindow(page =>
        {
            created.Add(page);
            return page == NavigationPage.Sessions ? view : new UserControl();
        });
        try
        {
            window.NavigateTo(NavigationPage.Sessions);
            view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
            item.DraftEarnings = "99";
            window.NavigateTo(NavigationPage.Settings);
            window.OpenGameDetails(new Game { Name = "Must not open while editing" });
            Assert.Same(view, ((ContentControl)window.FindName("MainContent")).Content);
            Assert.Equal(NavigationPage.Sessions, ((ListBoxItem)((ListBox)window.FindName("NavBar")).SelectedItem).Tag);
            Assert.DoesNotContain(NavigationPage.Settings, created);
            Assert.True(item.IsEditing);
            Assert.Equal(3m, item.Session.Earnings);
            Assert.Contains("Save changes or Cancel", ((TextBlock)view.FindName("EditorFeedback")).Text);
            item.CancelEdit();
            window.NavigateTo(NavigationPage.Settings);
            Assert.Equal(NavigationPage.Settings, ((ListBoxItem)((ListBox)window.FindName("SettingsNav")).SelectedItem).Tag);
            window.NavigateTo(NavigationPage.Dashboard);
            Assert.Equal(2, created.Count(page => page == NavigationPage.Dashboard));
            var modal = (EarningsTracker.Controls.Modal)window.FindName("MainModal");
            modal.Open("Synthetic import", new UserControl());
            window.NavigateTo(NavigationPage.Payments);
            Assert.DoesNotContain(NavigationPage.Payments, created);
            modal.Close();
            window.NavigateTo(NavigationPage.Payments);
            Assert.Contains(NavigationPage.Payments, created);
            await Task.CompletedTask;
        }
        finally { item.CancelEdit(); window.Close(); }
    });

    [Fact]
    [Trait("Category", "Regression")]
    public Task ImportWhileInlineDraftIsOpenRejectsStaleSaveAndCanRestartEditing() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var recorded = new DateTime(2026, 6, 5, 12, 0, 0);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([new Session { GameName = "Synthetic concurrency",
            Date = recorded, RecordedDate = recorded, Duration = TimeSpan.FromMinutes(1), Status = SessionStatus.Pending }]));
        var view = new SessionsView(database.Options) { Width = 740, Height = 680 };
        var model = (SessionsViewModel)view.DataContext;
        await view.RefreshSessionsAsync();
        var item = Assert.Single(model.Sessions);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        item.DraftDurationMinutes = "2";
        using (var payouts = new PayoutImporter(database.CreateContext()))
            payouts.ImportPayouts(payouts.PreviewImport(PayoutTextParser.ParseDetailed(
                "|6/6/2026|6/5/2026 12:00|Earning|Synthetic concurrency — 1 min recording (force-verified)|Verified|+$1.50|")));
        await view.RefreshSessionsAsync();
        Assert.Same(item, Assert.Single(model.Sessions));
        Assert.Equal("2", item.DraftDurationMinutes);
        Assert.False(item.TryCreateEditedSession(out _, out var error));
        item.ShowEditError(error);
        await Layout(view, 740, 680);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text.Contains("changed while you were editing"));
        Save(view, "session-stale-draft");
        item.CancelEdit(); item.BeginEdit();
        Assert.True(item.TryCreateEditedSession(out var edited, out error), error);
        Assert.True(edited.IsForceVerified);
        Assert.Equal(1.50m, edited.Earnings);
        Assert.Equal(new DateTime(2026, 6, 6), edited.PostedDate);
    });

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
        Assert.Empty(Descendants<DatePicker>((Border)view.FindName("DetailsPanel"))); // No editor controls are built for browsing rows.
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
        var details = (Border)view.FindName("DetailsPanel");
        foreach (var button in Descendants<Button>(details).Where(button => button.Content is string text && (text == "Edit session" || text == "Delete")))
        {
            var right = button.TranslatePoint(new Point(button.ActualWidth, 0), view).X;
            Assert.InRange(right, 0, width);
            Assert.True(button.ActualWidth > 40);
        }
        Save(view, $"sessions-{width}");
        Assert.Contains(Descendants<TextBlock>(list), text => text.Text == "Not Uploaded");
        var editing = (SessionItemViewModel)list.Items[0];
        view.SelectSession(editing);
        editing.BeginEdit();
        await Layout(view, width, 680);
        list.ScrollIntoView(editing);
        await Layout(view, width, 680);
        Save(view, $"sessions-editor-{width}");
        Assert.Equal(2, Descendants<DatePicker>((Border)view.FindName("DetailsPanel")).Count());
        editing.CancelEdit();
        await Layout(view, width, 680);
        Assert.Empty(Descendants<DatePicker>((Border)view.FindName("DetailsPanel")));
        ((TextBox)view.FindName("SearchSessionsBox")).Text = "no matching game";
        await Layout(view, width, 680);
        Assert.True(model.SessionsView.IsEmpty);
        Assert.Equal(Visibility.Visible, ((Button)view.FindName("ClearSessionFiltersButton")).Visibility);
        Save(view, $"sessions-filtered-empty-{width}");
        ((Button)view.FindName("ClearSessionFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, list.Items.Count);
    });

    [Fact]
    [Trait("Category", "Regression")]
    public Task ImportedUnlinkedRecordingUsesAmbiguousFilterAndExistingGameEditor() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var games = new List<Game>
        {
            new() { Id = 1, Name = "Example", RatePerHour = 5m },
            new() { Id = 2, Name = "Example", RatePerHour = 5m }
        };
        using (var seed = database.CreateContext()) { seed.Games.AddRange(games); seed.SaveChanges(); }
        var importer = new SessionImporter(database.Options);
        const string text = "Example\nExample · Jun 5, 2026 12:00 PM · 1h 0m · File deleted\n+$5.00\nVerified";
        importer.ImportSessions(importer.PreviewImport(SessionTextParser.Parse(text, games, null)));
        var view = new SessionsView(database.Options) { Width = 740, Height = 560 };
        var model = (SessionsViewModel)view.DataContext;
        await view.RefreshSessionsAsync();
        model.SelectStatus(model.StatusFilters.Single(filter => filter.OnlyUnlinked));
        var item = Assert.Single(model.SessionsView.Cast<SessionItemViewModel>());
        Assert.Equal(SessionStatus.Verified, item.Session.Status);
        Assert.True(item.CanSelectGame);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        item.DraftGame = games[1];
        Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
        SessionService.Update(item.Session.Id, edited, database.Options);
        importer.ImportSessions(importer.PreviewImport(SessionTextParser.Parse(text, games, null)));
        await view.RefreshSessionsAsync();
        Assert.True(model.SessionsView.IsEmpty);
        using var read = database.CreateContext();
        Assert.Equal(2, Assert.Single(read.Sessions).GameId);
        Assert.Equal(2, read.SessionChanges.Count());
    });

    [Fact]
    [Trait("Category", "Regression")]
    public Task ConflictDetailsShowBothRowsAndKeepLargeListsBounded() => host.RunAsync(async () =>
    {
        var summary = new ImportResultModal { Width = 600, FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        var result = new ImportResult();
        for (int i = 0; i < 1000; i++)
            result.AmbiguousSessions.Add(new Session
            {
                GameName = "Assassin's Creed Odyssey",
                RecordedDate = new DateTime(2026, 9, 28, 19, 0, 0),
                Duration = i % 2 == 0 ? TimeSpan.FromMinutes(65) : TimeSpan.Zero,
                Earnings = i % 2 == 0 ? 1.65m : 0m,
                Status = i % 2 == 0 ? SessionStatus.Verified : SessionStatus.NotUploaded
            });
        summary.ShowPreview(result);
        var section = (Expander)summary.FindName("ConflictsSection");
        var list = (ListBox)summary.FindName("ConflictRows");
        Assert.Equal(Visibility.Visible, section.Visibility);
        Assert.Equal("View conflicts (1000)", section.Header);
        Assert.False(section.IsExpanded);
        Assert.Equal(1000, list.Items.Count);
        Assert.Contains("Assassin's Creed Odyssey", (string)list.Items[0]);
        Assert.Contains("1h 5m · $1.65 · Verified", (string)list.Items[0]);
        Assert.Contains("<1m · $0.00 · Not uploaded", (string)list.Items[1]);
        section.IsExpanded = true;
        await Layout(summary, 600, 680);
        Assert.InRange(list.ActualHeight, 1, 240);
        Assert.InRange(Descendants<ListBoxItem>(list).Count(), 1, 50);
        Save(summary, "session-import-conflicts");
        summary.ShowPreview(new ImportResult());
        Assert.Equal(Visibility.Collapsed, section.Visibility);
        Assert.Empty(list.Items);
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
        Assert.Contains("1 ambiguous game matches will be saved unlinked", ((TextBlock)summary.FindName("AmbiguousSessionsText")).Text);
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
        if (element is SessionsView view) await view.EvidenceCompletion;
        element.UpdateLayout();
    }

    private sealed class BindingTrace : IDisposable
    {
        private readonly StringWriter _output = new();
        private readonly System.Diagnostics.TextWriterTraceListener _listener;
        private readonly System.Diagnostics.SourceLevels _previous;

        public BindingTrace()
        {
            _listener = new(_output);
            var source = System.Diagnostics.PresentationTraceSources.DataBindingSource;
            _previous = source.Switch.Level;
            source.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            source.Listeners.Add(_listener);
        }

        public void Dispose()
        {
            var source = System.Diagnostics.PresentationTraceSources.DataBindingSource;
            source.Listeners.Remove(_listener);
            source.Switch.Level = _previous;
            _listener.Flush();
            var messages = _output.ToString();
            _listener.Dispose();
            _output.Dispose();
            Assert.True(string.IsNullOrWhiteSpace(messages), messages);
        }
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
