using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentIcons.Wpf;
using EarningsTracker.Models;
using EarningsTracker.Enums;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class GamesRefinementTests(WpfTestHost host)
{
    private static void Seed(TestDatabase database)
    {
        using var db = database.CreateContext();
        db.Games.AddRange(new Game { Name = "Regular game with a very long complete edition and expansion title", RatePerHour = 1.25m, MinutesCap = 90.5m },
            new Game { Name = "Bookmarked game", RatePerHour = 2m, MinutesCap = 120m },
            new Game { Name = "Archived game with a long title", IsArchived = true, RatePerHour = 3m, MinutesCap = 180m });
        db.SaveChanges();
        var bookmarked = db.Games.Single(g => g.Name == "Bookmarked game");
        db.Bookmarks.Add(new Bookmark { GameId = bookmarked.Id });
        db.GameOwnership.Add(new GameOwnership { GameId = bookmarked.Id, IsOwned = true });
        db.SaveChanges();
    }

    [Fact]
    public Task LibraryCycleProgressUsesOnlyVerifiedSessionsAfterTheSavedResetDate() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            var game = new Game { Name = "Cycle progress", RatePerHour = 2m, MinutesCap = 120m, CapResetDate = new DateTime(2026, 10, 1) };
            db.Games.Add(game);
            db.SaveChanges();
            db.Sessions.AddRange(
                new Session { GameId = game.Id, GameName = game.Name, Date = new DateTime(2026, 10, 2), RecordedDate = new DateTime(2026, 10, 2), Duration = TimeSpan.FromMinutes(45), Status = SessionStatus.Verified },
                new Session { GameId = game.Id, GameName = game.Name, Date = new DateTime(2026, 10, 3), RecordedDate = new DateTime(2026, 10, 3), Duration = TimeSpan.FromMinutes(40), Status = SessionStatus.Pending },
                new Session { GameId = game.Id, GameName = game.Name, Date = new DateTime(2026, 9, 30), RecordedDate = new DateTime(2026, 9, 30), Duration = TimeSpan.FromMinutes(60), Status = SessionStatus.Verified });
            db.SaveChanges();
        }

        var item = Assert.Single(new GamesViewModel(database.Options).Games);
        Assert.True(item.HasCycleProgress);
        Assert.Equal(37.5m, item.CycleProgressPercent);
        Assert.Equal("0.75 / 2h", item.CycleProgressText);
        Assert.Equal("In progress", item.CycleStatusText);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, "0 / 50h", "Not started", false)]
    [InlineData(750, "12.5 / 50h", "In progress", true)]
    [InlineData(3000, "50 / 50h", "Cap reached", true)]
    [InlineData(3105, "51.75 / 50h", "Cap reached", true)]
    public void CompactCycleRetainsExactProgressAndOverCapHours(int minutes, string text, string status, bool visible)
    {
        var item = new GameItemViewModel(new Game { MinutesCap = 3000m }, false);
        item.SetCycleMinutes(minutes);
        Assert.Equal(text, item.CycleProgressText);
        Assert.Equal(status, item.CycleStatusText);
        Assert.Equal(visible, item.HasCycleProgress);
    }

    [Fact]
    public void LibraryAcquisitionKeepsDatedExpiredEvidenceWithoutEditionClutter()
    {
        var item = new GameItemViewModel(new Game(), false);
        var offer = CatalogTests.Offer();
        offer.Edition = "Deluxe edition";
        offer.VerifiedAt = new DateTime(2026, 1, 29);
        offer.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        item.SetCatalog(null, [], [offer], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Contains("Steam", item.LibraryPriceEvidence);
        Assert.Contains("Checked Jan 29, 2026", item.LibraryPriceEvidence);
        Assert.Contains("Expired", item.LibraryPriceEvidence);
        Assert.DoesNotContain("Deluxe", item.LibraryPriceEvidence);
        Assert.Contains("Deluxe", item.PriceEvidence);
        Assert.Contains("Deluxe", Assert.Single(item.OfferDetails).Details);
    }

    [Fact]
    public Task InactiveStateChangesAndUnchangedInputsDoNotResetView() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        var original = model.Games.ToArray();
        var resets = 0;
        model.GamesView.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        model.SearchGames(""); model.FilterBookmarked(false);
        model.SelectedGenre = "All genres"; model.SelectedStore = "All stores";
        model.FreeOnly = false; model.OwnedOnly = false; model.PlayableOnly = false; model.GamePassOnly = false;
        var item = original[0];
        item.ToggleBookmarkCommand.Execute(null);
        item.ToggleOwnedCommand.Execute(null);
        Assert.Equal(0, resets);
        Assert.Equal(2, model.BookmarkCount);
        Assert.Equal(3, model.AvailableGamesCount);
        model.SearchGames("Regular");
        var afterSearch = resets;
        model.SearchGames("Regular");
        Assert.Equal(afterSearch, resets);
        Assert.Same(item, Assert.Single(model.GamesView.Cast<GameItemViewModel>()));
        Assert.Equal(original, model.Games.ToArray());
        using var read = database.CreateContext();
        Assert.True(read.GameOwnership.Find(item.Game.Id)!.IsOwned);
        Assert.Contains(read.Bookmarks, b => b.GameId == item.Game.Id);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OwnershipChangesStillUpdateActiveMembership(bool playable) => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        model.SearchGames("Regular"); model.FilterBookmarked(true);
        if (playable) model.PlayableOnly = true; else model.OwnedOnly = true;
        var item = model.Games[0];
        item.ToggleBookmarkCommand.Execute(null);
        Assert.Equal(0, model.AvailableGamesCount);
        item.ToggleOwnedCommand.Execute(null);
        Assert.Same(item, Assert.Single(model.GamesView.Cast<GameItemViewModel>()));
        item.ToggleOwnedCommand.Execute(null);
        Assert.Equal(0, model.AvailableGamesCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SavedGamesPreferencesApplyOnReopenWithoutChangingDefaults() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var settings = new AppSettings(database.Options);
        Assert.Equal(2, settings.GamesDefaultSort);
        Assert.False(settings.GamesBookmarksOnly);
        Assert.True(settings.GamesShowArchived);
        var initial = new GamesViewModel(database.Options);
        Assert.Equal(3, initial.AvailableGamesCount);
        settings.GamesDefaultSort = 0;
        settings.GamesBookmarksOnly = true;
        settings.GamesShowArchived = false;
        var reopened = new GamesViewModel(database.Options);
        Assert.Equal(0, reopened.DefaultSortIndex);
        Assert.True(reopened.BookmarkedOnly);
        Assert.False(reopened.ShowArchived);
        Assert.Equal("Game.RatePerHour", reopened.GamesView.SortDescriptions[1].PropertyName);
        Assert.Equal("Bookmarked game", Assert.Single(reopened.GamesView.Cast<GameItemViewModel>()).Game.Name);
        reopened.FilterBookmarked(false);
        Assert.Equal(2, reopened.AvailableGamesCount);
        Assert.DoesNotContain(reopened.GamesView.Cast<GameItemViewModel>(), x => x.Game.IsArchived);
        settings.Set("games.defaultSort", "invalid");
        Assert.Equal(2, new GamesViewModel(database.Options).DefaultSortIndex);
        return Task.CompletedTask;
    });

    [Fact]
    public Task InitialNewestOrderMatchesExplicitSortAndKeepsArchivedLast() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var model = new GamesViewModel(database.Options);
        var initial = model.GamesView.Cast<GameItemViewModel>().ToArray();
        Assert.Equal("Bookmarked game", initial[0].Game.Name);
        Assert.True(initial[^1].Game.IsArchived);
        model.SortGames("Game.Id", ListSortDirection.Descending);
        Assert.Equal(initial, model.GamesView.Cast<GameItemViewModel>().ToArray());
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(740)]
    [InlineData(940)]
    [InlineData(1480)]
    public Task RenderCardsWithReadableStatesAndLoadedArtwork(double width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var view = new GamesView(database.Options)
        {
            FontFamily = (FontFamily)Application.Current.FindResource("AppFont"),
            Background = (Brush)Application.Current.FindResource("DefaultBackgroundColor")
        };
        view.Measure(new Size(width, 768)); view.Arrange(new Rect(0, 0, width, 768)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)view.FindName("GamesList");
        var artworkStyle = (Style)Application.Current.FindResource("ArtworkPresenter");
        // A frozen synthetic cover exercises the loaded template without cache writes/provider calls.
        var cover = BitmapSource.Create(60, 80, 96, 96, PixelFormats.Bgr32, null,
            Enumerable.Repeat(unchecked((int)0x00565EAA), 60 * 80).ToArray(), 60 * 4);
        cover.Freeze();
        foreach (var container in Descendants<ListBoxItem>(list))
        {
            var item = (GameItemViewModel)container.DataContext;
            if (!view.IsCompactLibrary)
            {
                var row = Descendants<Grid>(container).First();
                Assert.InRange(row.RowDefinitions[1].ActualHeight, 0, 0.01);
            }
            var comparisons = Descendants<Grid>(container).Single(grid => grid.Name == "Comparisons");
            Assert.Equal(3, comparisons.ColumnDefinitions.Count);
            Assert.DoesNotContain(Descendants<TextBlock>(container), text => text.Text == "Full-cycle potential");
            Assert.Equal(item.HasCycleProgress ? Visibility.Visible : Visibility.Collapsed,
                Assert.Single(Descendants<ProgressBar>(container)).Visibility);
            Assert.Equal(1d, container.Opacity);
            var bookmark = Descendants<Button>(container).Single(b => b.Command == item.ToggleBookmarkCommand);
            var icon = Assert.Single(Descendants<FluentIcon>(bookmark));
            Assert.Equal(item.IsBookmarked ? "Filled" : "Regular", icon.IconVariant.ToString());
            Assert.Equal(bookmark.Foreground, icon.Foreground);
            Assert.Equal(Application.Current.FindResource(item.IsBookmarked ? "AccentTextColor" : "SecondaryTextColor"), bookmark.Foreground);
            var ownershipError = Descendants<TextBlock>(container).Single(t => t.Style == Application.Current.FindResource("InlineValidationText"));
            Assert.Equal(Visibility.Collapsed, ownershipError.Visibility);
            var title = Descendants<TextBlock>(container).Single(t => t.Text == item.Game.Name);
            Assert.True(title.ActualWidth > 0);
            if (item.Game.IsArchived)
                Assert.Equal(12d, Descendants<TextBlock>(container).Single(t => t.Text == "Archived").FontSize);
            if (item.IsBookmarked) Descendants<ContentControl>(container).Single(c => c.Style == artworkStyle || c.Style?.BasedOn == artworkStyle).Content = cover;
        }
        view.UpdateLayout();
        var directory = Path.Combine(AppContext.BaseDirectory, "GamesVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)width, 768, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"games-refined-{width}.png")); encoder.Save(file);
    });

    [Fact]
    public Task ClearFiltersUpdatesBoundBookmarkAndKeepsSavedStartingPreferences() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var settings = new AppSettings(database.Options) { GamesBookmarksOnly = true, GamesShowArchived = false, GamesDefaultSort = 1 };
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        var bookmark = (CheckBox)view.FindName("BookmarkFilter");
        var clear = (Button)view.FindName("ClearFiltersButton");
        var sort = (ComboBox)view.FindName("SortComboBox");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(bookmark.IsChecked);
        Assert.True(clear.IsEnabled);
        Assert.Equal(1, model.AvailableGamesCount);
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(bookmark.IsChecked);
        Assert.False(model.BookmarkedOnly);
        Assert.True(BindingOperations.IsDataBound(bookmark, ToggleButton.IsCheckedProperty));
        Assert.False(clear.IsEnabled);
        Assert.False(model.ShowArchived);
        Assert.Equal(1, sort.SelectedIndex);
        Assert.Equal(2, model.AvailableGamesCount);

        bookmark.IsChecked = true;
        Assert.True(model.BookmarkedOnly);
        ((TextBox)view.FindName("SearchBox")).Text = "Bookmarked";
        model.FreeOnly = model.OwnedOnly = model.SteamOnly = model.GamePassOnly = model.PlayableOnly = model.MultiplayerOnly = true;
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("", ((TextBox)view.FindName("SearchBox")).Text);
        Assert.False(bookmark.IsChecked);
        Assert.False(((CheckBox)view.FindName("MultiplayerFilter")).IsChecked);
        Assert.False(model.CanClearFilters);
        Assert.Equal(0, model.SecondaryFilterCount);
        Assert.Equal(2, model.AvailableGamesCount);
        var reopened = new GamesViewModel(database.Options);
        Assert.True(reopened.BookmarkedOnly);
        Assert.False(reopened.ShowArchived);
        Assert.Equal(1, reopened.DefaultSortIndex);
    });

    [Theory]
    [InlineData(740)]
    [InlineData(1212)]
    public Task SecondaryFiltersStayOpenAndToolbarWrapsWithoutClipping(double width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        Seed(database);
        var view = new GamesView(database.Options) { FontFamily = (FontFamily)Application.Current.FindResource("AppFont") };
        var window = new Window { Content = view, Width = width, Height = 768, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var toolbar = (Border)view.FindName("FilterToolbar");
            Assert.InRange(toolbar.ActualHeight, 76, width < 1000 ? 130 : 90);
            foreach (var control in Descendants<Control>(toolbar).Where(control => control.TemplatedParent == null && control is CheckBox or ComboBox or Button or TextBox))
            {
                var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                Assert.InRange(bounds.Right, 0, view.ActualWidth);
                Assert.InRange(control.ActualHeight, 32, 36);
            }
            var more = (Button)view.FindName("MoreFiltersButton");
            var popup = (Popup)view.FindName("MoreFiltersPopup");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var free = (CheckBox)view.FindName("FreeFilter");
            Assert.True(free.IsKeyboardFocused);
            foreach (var name in new[] { "FreeFilter", "SteamFilter", "GamePassFilter" })
                ((CheckBox)view.FindName(name)).IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var model = (GamesViewModel)view.DataContext;
            Assert.True(popup.IsOpen);
            Assert.Equal(4, model.SecondaryFilterCount);
            Assert.Equal("More filters (4)", model.MoreFiltersText);
            Assert.True(model.CanClearFilters);
            var panel = (StackPanel)view.FindName("MoreFiltersPanel");
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(panel));
            var directory = Path.Combine(AppContext.BaseDirectory, "GamesVisuals");
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)popup.Child.RenderSize.Width, (int)popup.Child.RenderSize.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(popup.Child);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(directory, $"games-more-filters-{width}.png"))) encoder.Save(file);
            free.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(free)!, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(popup.IsOpen);
            Assert.True(more.IsKeyboardFocused);
            Assert.True(model.FreeOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MultiplayerCombinesWithOwnershipSearchAndArchivedVisibilityWithoutGuessing() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            var snapshot = new CatalogSnapshot { Version = "multiplayer-fixture", PublishedAt = DateTime.UtcNow.AddMinutes(-1) };
            foreach (var (name, support, archived, owned) in new (string, bool?, bool, bool)[]
            {
                ("Friends owned", true, false, true), ("Friends unowned", true, false, false),
                ("Friends archived", true, true, true), ("Solo", false, false, true), ("Multiplayer in description only", null, false, true)
            })
            {
                var game = new Game { Name = name, IsArchived = archived };
                db.Games.Add(game); db.SaveChanges();
                if (owned) db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true });
                snapshot.Games.Add(new CatalogEntry { CatalogId = name, Name = name, Reviewed = true, Summary = "Multiplayer co-op",
                    SupportsMultiplayer = support, MultiplayerProvenance = support.HasValue ? "Synthetic structured evidence" : "",
                    MultiplayerVerifiedAt = support.HasValue ? DateTime.UtcNow.AddHours(-1) : null });
            }
            db.SaveChanges();
            CatalogService.Import(db, snapshot);
        }
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        var checkbox = (CheckBox)view.FindName("MultiplayerFilter");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        checkbox.IsChecked = true;
        Assert.True(model.MultiplayerOnly);
        Assert.Equal(3, model.AvailableGamesCount);
        Assert.True(model.CanClearFilters);
        model.OwnedOnly = true; model.ShowArchived = false;
        Assert.Equal("Friends owned", Assert.Single(model.GamesView.Cast<GameItemViewModel>()).Game.Name);
        model.SearchGames("unowned"); Assert.Equal(0, model.AvailableGamesCount);
        model.ClearFilters();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(checkbox.IsChecked);
        Assert.Equal(4, model.AvailableGamesCount);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
