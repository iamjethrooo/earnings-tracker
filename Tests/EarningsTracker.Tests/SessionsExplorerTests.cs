using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class SessionsExplorerTests(WpfTestHost host)
{
    [Fact]
    public Task FiltersComposeWithoutMergingDuplicateGameIdentities() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Id = 1, Name = "Same name" }, new Game { Id = 2, Name = "Same name", IsArchived = true });
            db.Payouts.Add(new Payout { Id = 1, SourceId = "SOURCE-42" });
            db.Sessions.AddRange(
                Recording(1, 1, new DateTime(2026, 10, 20), SessionStatus.Verified, 2m),
                Recording(2, 2, new DateTime(2026, 10, 14), SessionStatus.Verified, 10m),
                Recording(3, null, new DateTime(2026, 10, 13), SessionStatus.Pending, 1m),
                Recording(4, 1, new DateTime(2026, 9, 20), SessionStatus.Pending, 0m),
                Recording(5, 1, new DateTime(2026, 10, 21), SessionStatus.Verified, 3m));
            db.Sessions.Local.Single(session => session.Id == 1).PayoutId = 1;
            db.SaveChanges();
        }
        var model = new SessionsViewModel(database.Options) { FilterToday = new DateTime(2026, 10, 20) };
        await model.LoadSessionsAsync();
        Assert.Null(model.LoadError);
        Assert.Equal(5, model.SessionsView.Cast<object>().Count()); // All historical dates remain the default.
        Assert.Equal(4, model.GameFilters.Count);
        model.SelectedDateIndex = 1;
        Assert.Equal(new[] { 1, 2 }, model.SessionsView.Cast<SessionItemViewModel>().Select(item => item.Session.Id));
        model.SelectedGameFilter = model.GameFilters.Single(filter => filter.GameId == 2);
        Assert.Equal(2, Assert.Single(model.SessionsView.Cast<SessionItemViewModel>()).Session.Id);
        model.SelectStatus(model.StatusFilters.Single(filter => filter.Status == SessionStatus.Pending));
        Assert.True(model.SessionsView.IsEmpty);
        model.ClearFilters();
        model.SearchText = "SOURCE-42";
        Assert.Equal(1, Assert.Single(model.SessionsView.Cast<SessionItemViewModel>()).Session.Id);
        model.ClearFilters();
        model.SelectedSortIndex = 3;
        Assert.Equal(2, model.SessionsView.Cast<SessionItemViewModel>().First().Session.Id);
        model.SelectedDateIndex = 2;
        Assert.Equal(3, model.SessionsView.Cast<object>().Count());
        model.SelectStatus(model.StatusFilters.Single(filter => filter.OnlyUnlinked));
        Assert.Equal(3, Assert.Single(model.SessionsView.Cast<SessionItemViewModel>()).Session.Id);
        model.ClearFilters();
        Assert.Equal(3, model.SelectedSortIndex); // Clear keeps the user's sorting choice.
        Assert.Equal(5, model.SessionsView.Cast<object>().Count());
    });

    [Theory]
    [InlineData(740, 560)]
    [InlineData(1212, 900)]
    [InlineData(1572, 940)]
    public Task TableDrawerAndEvidenceRenderWithPinnedActions(int width, int height) => host.RunAsync(async () =>
    {
        using var trace = new BindingTrace();
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([new Session
        {
            GameName = "A long synthetic game title with edition and preserved source evidence", Date = new DateTime(2026, 10, 20),
            RecordedDate = new DateTime(2026, 10, 20), Duration = TimeSpan.FromSeconds(61).Add(TimeSpan.FromTicks(7)),
            Earnings = 1.2345m, Status = SessionStatus.NotUploaded, ImportSourceText = "Original synthetic source · precision 1.2345"
        }]));
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        await Layout(view, width, height);
        Capture(view, $"table-{width}");
        var model = (SessionsViewModel)view.DataContext;
        var list = (ListView)view.FindName("SessionsList");
        var item = Assert.Single(model.Sessions);
        list.SelectedItem = item;
        await view.RefreshSessionsAsync();
        await Layout(view, width, height);
        Assert.Null(model.LoadError);
        Assert.Equal("Session import", item.SourceLabel);
        Assert.Equal("Not recorded", item.SourceId);
        Assert.Contains("Unknown", item.PostedText);
        Assert.Equal("Original synthetic source · precision 1.2345", Assert.Single(item.Evidence).SourceText);
        Assert.Contains("1.2345", item.Evidence[0].AfterValuesJson);
        var details = (Border)view.FindName("DetailsPanel");
        Assert.Equal(width < 1190, ReferenceEquals(details.Parent, view.FindName("DrawerLayer")));
        var status = Descendants<TextBlock>(list).Single(text => text.Text == "Not Uploaded");
        Assert.True(status.ActualWidth >= 85);
        Capture(view, $"selected-{width}");
        if (width == 1212) Capture(view, "selected-150pct", 1.5);
        ((Button)view.FindName("EditSessionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, width, height);
        var scroll = (ScrollViewer)view.FindName("DetailsScroll");
        Descendants<TextBox>(details).Single(box => Equals(box.Tag, nameof(item.DraftEarnings))).Text = "-2";
        scroll.ScrollToBottom();
        await Layout(view, width, height);
        var save = Descendants<Button>(details).Single(button => Equals(button.Content, "Save changes"));
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, width, height);
        Assert.True(item.IsEditing);
        Assert.NotNull(item.EarningsError);
        var error = Descendants<TextBlock>(details).Single(text => text.Text == item.EarningsError);
        var errorBounds = error.TransformToAncestor(scroll).TransformBounds(new Rect(error.RenderSize));
        Assert.InRange(errorBounds.Top, 0, scroll.ViewportHeight);
        Assert.InRange(errorBounds.Bottom, 0, scroll.ViewportHeight + 1);
        var saveBounds = save.TransformToAncestor(view).TransformBounds(new Rect(save.RenderSize));
        Assert.InRange(saveBounds.Bottom, 0, height);
        Assert.InRange(saveBounds.Right, 0, width);
        Capture(view, $"validation-{width}");
        ((Button)view.FindName("CloseDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(item, model.SelectedSession);
        Assert.Contains("Save changes or Cancel", ((TextBlock)view.FindName("PanelFeedbackText")).Text);
        item.CancelEdit();
        ((Button)view.FindName("CloseDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(model.SelectedSession);
        Assert.True(((Grid)view.FindName("BrowseContent")).IsEnabled);
        using var db = database.CreateContext();
        Assert.Equal(1.2345m, Assert.Single(db.Sessions).Earnings);
        Assert.Single(db.SessionChanges);
    });

    [Fact]
    public Task GameLinkNavigationKeepsTheSelectedSessionAndKeyboardOpensDetails() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Id = 1, Name = "First game" }, new Game { Id = 2, Name = "Second game" });
            db.Sessions.AddRange(Recording(1, 1, DateTime.Today, SessionStatus.Pending, 1), Recording(2, 2, DateTime.Today, SessionStatus.Verified, 2));
            db.SaveChanges();
        }
        var view = new SessionsView(database.Options);
        var window = new Window { Content = view, Width = 1600, Height = 900, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await view.RefreshSessionsAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var model = (SessionsViewModel)view.DataContext;
            var selected = model.Sessions.Single(item => item.Session.Id == 1);
            var list = (ListView)view.FindName("SessionsList");
            list.SelectedItem = selected;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Game? navigated = null;
            view.ViewGameRequested += (_, game) => navigated = game;
            var otherLink = Descendants<Button>(list).Single(button => button.DataContext is SessionItemViewModel item && item.Session.Id == 2);
            otherLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, navigated?.Id);
            Assert.Same(selected, list.SelectedItem);
            Assert.Same(selected, model.SelectedSession);
            Assert.False(selected.IsEditing);
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(selected);
            row.Focus();
            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.True(((Button)view.FindName("CloseDetailsButton")).IsKeyboardFocused);
            ((Button)view.FindName("EditSessionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var date = Descendants<DatePicker>(view).First();
            Assert.True(date.IsKeyboardFocusWithin);
            window.Width = 1000;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(date.IsKeyboardFocusWithin);
            Assert.False(((Grid)view.FindName("BrowseContent")).IsEnabled);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation((Border)view.FindName("DetailsPanel")));
            Descendants<Button>(view).Single(button => Equals(button.Content, "Cancel"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(((Button)view.FindName("EditSessionButton")).IsKeyboardFocused);
            ((Button)view.FindName("CloseDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Null(model.SelectedSession);
            Assert.True(list.IsEnabled);
            Assert.True(row.IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptyLoadingFailedAndStaleSnapshotsRemainDistinctAndRetryable() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var view = new SessionsView(database.Options);
        var model = (SessionsViewModel)view.DataContext;
        var load = view.RefreshSessionsAsync();
        Assert.True(model.IsLoading);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName("SessionsEmptyPanel")).Visibility);
        await load;
        Assert.Contains("No sessions yet", ((TextBlock)view.FindName("SessionsEmptyState")).Text);
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO HiddenMetadata");
        await view.RefreshSessionsAsync();
        Assert.NotNull(model.LoadError);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName("SessionsEmptyPanel")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)view.FindName("SessionsSummary")).Visibility);
        using (var db = database.CreateContext())
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE HiddenMetadata RENAME TO GameMetadata");
            db.Sessions.Add(Recording(1, null, DateTime.Today, SessionStatus.Pending, 1));
            db.SaveChanges();
        }
        await view.RefreshSessionsAsync();
        var item = Assert.Single(model.Sessions);
        model.SearchText = "does not match";
        Assert.Contains("No sessions match", ((TextBlock)view.FindName("SessionsEmptyState")).Text);
        model.ClearFilters();
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        item.DraftEarnings = "7";
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE GameMetadata RENAME TO HiddenMetadata");
        await view.RefreshSessionsAsync();
        Assert.Same(item, Assert.Single(model.Sessions));
        Assert.Same(item, model.SelectedSession);
        Assert.Equal("7", item.DraftEarnings);
        Assert.Contains("Previous rows may be out of date", ((TextBlock)view.FindName("SessionsLoadStatus")).Text);
        using (var db = database.CreateContext()) db.Database.ExecuteSqlRaw("ALTER TABLE HiddenMetadata RENAME TO GameMetadata");
        await view.RefreshSessionsAsync();
        Assert.Null(model.LoadError);
        Assert.True(item.IsEditing);
        item.CancelEdit();
    });

    [Fact]
    public Task LargePersistedHistoryKeepsRealizedRowsBounded() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Sessions.AddRange(Enumerable.Range(1, 10000).Select(id => Recording(id, null,
                new DateTime(2026, 10, 20).AddMinutes(-id), id % 2 == 0 ? SessionStatus.Verified : SessionStatus.Pending, id / 100m)));
            db.SaveChanges();
        }
        var timer = Stopwatch.StartNew();
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        await Layout(view, 1212, 900);
        var list = (ListView)view.FindName("SessionsList");
        Assert.Equal(10000, list.Items.Count);
        var firstCount = Descendants<ListViewItem>(list).Count();
        Assert.InRange(firstCount, 1, 150);
        Assert.All(Descendants<ListViewItem>(list), row => Assert.Equal(44, row.ActualHeight));
        var scroll = Descendants<ScrollViewer>(list).First();
        scroll.ScrollToEnd();
        await Layout(view, 1212, 900);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(9999));
        var lastCount = Descendants<ListViewItem>(list).Count();
        Assert.InRange(lastCount, 1, 150);
        var model = (SessionsViewModel)view.DataContext;
        model.SelectStatus(model.StatusFilters.Single(filter => filter.Status == SessionStatus.Verified));
        model.SelectedSortIndex = 3;
        Assert.Equal(5000, list.Items.Count);
        Assert.Equal(100m, ((SessionItemViewModel)list.Items[0]).Session.Earnings);
        scroll.ScrollToEnd();
        await Layout(view, 1212, 900);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(4999));
        Assert.InRange(Descendants<ListViewItem>(list).Count(), 1, 150);
        timer.Stop();
        Capture(view, "large-history-end");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "SessionsExplorerVisuals", "workload.txt"),
            $"10,000 persisted synthetic sessions; 1212x900 page; 44-DIP rows; load/layout/filter/sort/end-scroll {timer.ElapsedMilliseconds} ms; first {firstCount}, end {lastCount} realized rows. Not a production latency guarantee.");
    });

    [Fact]
    public Task ResizeAndImportedConflictKeepDraftAndRevealRecoveryFeedback() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var seed = database.CreateContext()) { seed.Games.Add(new Game { Id = 1, Name = "Synthetic game", RatePerHour = 1 }); seed.SaveChanges(); }
        var imported = Recording(0, null, new DateTime(2026, 10, 20), SessionStatus.Pending, 1.2345m);
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([imported]));
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        var model = (SessionsViewModel)view.DataContext;
        var item = Assert.Single(model.Sessions);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        await Layout(view, 1572, 940);
        ((Button)view.FindName("EditSessionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(view, 1572, 940);
        var game = Descendants<ComboBox>(view).Single(combo => System.Windows.Automation.AutomationProperties.GetName(combo) == "Session game");
        game.SelectedItem = model.AvailableGames.Single();
        Descendants<TextBox>(view).Single(box => Equals(box.Tag, nameof(item.DraftEarnings))).Text = "7.12345";
        var draftGame = item.DraftGame;
        await Layout(view, 740, 560);
        Assert.Same(draftGame, item.DraftGame);
        Assert.Equal("7.12345", item.DraftEarnings);
        await Layout(view, 1572, 940);
        Assert.Same(draftGame, item.DraftGame);
        Assert.True(item.IsEditing);
        imported.Status = SessionStatus.Verified;
        imported.Earnings = 2.3456m;
        importer.ImportSessions(importer.PreviewImport([imported]));
        await view.RefreshSessionsAsync();
        Assert.Same(item, model.SelectedSession);
        Assert.Equal("7.12345", item.DraftEarnings);
        Assert.Equal(1, item.DraftGame?.Id);
        await Layout(view, 740, 560);
        var scroll = (ScrollViewer)view.FindName("DetailsScroll");
        var save = Descendants<Button>(view).Single(button => Equals(button.Content, "Save changes"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            scroll.ScrollToBottom();
            await Layout(view, 740, 560);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(view, 740, 560);
            Assert.True(item.IsEditing);
            var feedback = (TextBlock)view.FindName("PanelFeedbackText");
            Assert.Contains("changed while you were editing", feedback.Text);
            var bounds = feedback.TransformToAncestor(scroll).TransformBounds(new Rect(feedback.RenderSize));
            Assert.InRange(bounds.Top, 0, scroll.ViewportHeight);
            Assert.InRange(bounds.Bottom, 0, scroll.ViewportHeight);
        }
        Capture(view, "narrow-conflict");
        using var db = database.CreateContext();
        Assert.Equal(2.3456m, Assert.Single(db.Sessions).Earnings);
        Assert.DoesNotContain(db.SessionChanges, change => change.ChangeType == "Updated");
        item.CancelEdit();
    });

    [Fact]
    public Task DeletedTargetKeepsDraftAndRestartGuardUntilCancel() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var importer = new SessionImporter(database.Options);
        importer.ImportSessions(importer.PreviewImport([Recording(0, null, DateTime.Today, SessionStatus.Pending, 1)]));
        var view = new SessionsView(database.Options);
        await view.RefreshSessionsAsync();
        var model = (SessionsViewModel)view.DataContext;
        var item = Assert.Single(model.Sessions);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        item.DraftEarnings = "9";
        SessionService.Delete(item.Session.Id, database.Options);
        await view.RefreshSessionsAsync();
        Assert.Empty(model.Sessions);
        Assert.Same(item, model.SelectedSession);
        Assert.True(view.HasActiveDraft);
        Assert.False(view.CanLeave());
        var window = new MainWindow(_ => view);
        try { Assert.True(window.IsEditorOpen); }
        finally { item.CancelEdit(); window.Close(); }
        await Layout(view, 740, 560);
        view.SelectSession(item);
        await view.EvidenceCompletion;
        item.BeginEdit();
        await Layout(view, 740, 560);
        Descendants<Button>(view).Single(button => Equals(button.Content, "Save changes"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(item.IsEditing);
        Assert.Contains("no longer exists", item.EditError);
        item.CancelEdit();
        using var db = database.CreateContext();
        Assert.Empty(db.Sessions);
        Assert.Single(db.SessionChanges.Where(change => change.ChangeType == "Deleted"));
    });

    private static Session Recording(int id, int? gameId, DateTime date, SessionStatus status, decimal earnings) => new()
    {
        Id = id, GameId = gameId, GameName = "Same name", Date = date, RecordedDate = date,
        Duration = TimeSpan.FromMinutes(id), Status = status, Earnings = earnings
    };

    private static async Task Layout(FrameworkElement element, double width, double height)
    {
        if (element is SessionsView sessions)
        {
            sessions.FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
            sessions.Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor");
            sessions.UseLayoutRounding = true;
        }
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (element is SessionsView view) await view.EvidenceCompletion;
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Capture(FrameworkElement view, string name, double scale = 1)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "SessionsExplorerVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)(view.ActualWidth * scale), (int)(view.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private sealed class BindingTrace : IDisposable
    {
        private readonly StringWriter _output = new();
        private readonly TextWriterTraceListener _listener;
        private readonly SourceLevels _previous;
        public BindingTrace()
        {
            _listener = new(_output);
            _previous = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
        }
        public void Dispose()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = _previous;
            _listener.Flush();
            var messages = _output.ToString();
            _listener.Dispose();
            _output.Dispose();
            Assert.True(string.IsNullOrWhiteSpace(messages), messages);
        }
    }
}
