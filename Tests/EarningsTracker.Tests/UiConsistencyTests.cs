using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Markup;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;
using EarningsTracker.Views.Modals;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class UiConsistencyTests(WpfTestHost host)
{
    private static void SeedGames(TestDatabase database)
    {
        using var db = database.CreateContext();
        db.Games.AddRange(new Game { Name = "Example", RatePerHour = 1.5m }, new Game { Name = "Other" });
        db.SaveChanges();
    }

    [Fact]
    public Task BookmarkSaveFailureKeepsStateAndAllowsRetry() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        SeedGames(database);
        var model = new GamesViewModel(database.Options);
        var item = model.Games.First();
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_bookmark BEFORE INSERT ON Bookmarks BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        var error = Record.Exception(() => item.ToggleBookmarkCommand.Execute(null));
        Assert.False(item.IsBookmarked);
        Assert.Null(error);
        Assert.Equal(0, model.BookmarkCount);
        using (var db = database.CreateContext())
        {
            Assert.Empty(db.Bookmarks);
            db.Database.ExecuteSqlRaw("DROP TRIGGER reject_bookmark;");
        }
        item.ToggleBookmarkCommand.Execute(null);
        Assert.True(item.IsBookmarked);
        using var read = database.CreateContext();
        Assert.Single(read.Bookmarks);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BookmarkToggleRefreshesCountsAndActiveSearchFilter() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        SeedGames(database);
        var model = new GamesViewModel(database.Options);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var item = model.Games.Single(g => g.Game.Name == "Example");
        model.SearchGames("Example");
        model.FilterBookmarked(true);
        Assert.Equal(0, model.AvailableGamesCount);
        item.ToggleBookmarkCommand.Execute(null);
        Assert.Equal("My Bookmarks (1)", model.BookmarkText);
        Assert.Equal(1, model.AvailableGamesCount);
        Assert.Contains(nameof(model.BookmarkText), notifications);
        Assert.Contains(nameof(model.BookmarkCount), notifications);
        item.ToggleBookmarkCommand.Execute(null);
        Assert.Equal(0, model.AvailableGamesCount);
        Assert.Equal("My Bookmarks (0)", model.BookmarkText);
        using var read = database.CreateContext();
        Assert.Empty(read.Bookmarks);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-PH")]
    public Task EarningsDisplaysStayInUsd(string culture) => host.RunAsync(() =>
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var session = new Session { Earnings = 1234.5m, Status = SessionStatus.Verified };
            Assert.Equal("$1,234.50", session.EarningsText);
            var details = new GameDetailsViewModel(new Game { Name = "Example", RatePerHour = 2m, MinutesCap = 60m }, []);
            Assert.Equal("$2.00 remaining · $2.00 maximum", details.EarningsDescription);
            var result = new ImportResult();
            result.NewSessions.Add(session);
            var modal = new ImportResultModal();
            modal.ShowPreview(result);
            Assert.Equal("Total session earnings: $1,234.50", ((TextBlock)modal.FindName("TotalEarningsText")).Text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Added", "Bonus", "$1,234.50")]
    [InlineData("Updated", "Payment", "-$1,234.50")]
    [InlineData("Unchanged", "Payment", "-$1,234.50")]
    public Task PayoutSummaryUsesUsd(string category, string type, string expected) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var first = $"|6/5/2026|—|{type}|Example|Processing|-$1234.50|";
        if (category != "Added")
        {
            using var importer = new PayoutImporter(database.CreateContext());
            importer.ImportPayouts(importer.PreviewImport(PayoutTextParser.ParseDetailed(first)));
        }
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var view = new ImportPayoutsView(database.Options);
            ((TextBox)view.FindName("ImportTextBox")).Text = category == "Added"
                ? first.Replace("-$1234.50", "+$1234.50") : category == "Updated" ? first.Replace("Processing", "Paid") : first;
            ((Button)view.FindName("ParseImportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.OperationCompletion;
            await Dispatcher.Yield();
            var panels = (StackPanel)view.FindName("ImportSummarySections");
            var section = panels.Children.OfType<Expander>().Single(e => ((TextBlock)e.Header).Text.StartsWith(category));
            section.IsExpanded = true;
            section.Measure(new Size(1000, 1000));
            section.Arrange(new Rect(0, 0, 1000, 1000));
            section.UpdateLayout();
            var text = string.Join(Environment.NewLine, Assert.IsType<ListBox>(section.Content).Items.Cast<string>());
            Assert.Contains(expected, text);
            Assert.DoesNotContain("€", text);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    [Fact]
    public Task ActualDatePickerCalendarUsesTheSharedThemeAndRetainsRestrictions() => host.RunAsync(async () =>
    {
        var picker = new DatePicker { SelectedDate = new DateTime(2026, 6, 5), DisplayDate = new DateTime(2026, 6, 5),
            Style = (Style)Application.Current.FindResource(typeof(DatePicker)),
            DisplayDateStart = new DateTime(2026, 6, 1), DisplayDateEnd = new DateTime(2026, 6, 30) };
        picker.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 6, 10)));
        picker.ApplyTemplate();
        var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
        var calendar = Assert.IsType<System.Windows.Controls.Calendar>(popup.Child);
        calendar.ApplyTemplate();
        calendar.Measure(new Size(400, 400));
        calendar.Arrange(new Rect(0, 0, 400, 400));
        calendar.UpdateLayout();
        await Dispatcher.Yield();
        var item = Assert.Single(Descendants<CalendarItem>(calendar));
        Assert.Equal(Color.FromRgb(26, 26, 30), ((SolidColorBrush)item.Background).Color);
        Assert.Same(Application.Current.FindResource("TrackerCalendarDay"), calendar.CalendarDayButtonStyle);
        Assert.Same(Application.Current.FindResource("TrackerCalendarMonth"), calendar.CalendarButtonStyle);
        Assert.Equal(picker.SelectedDate, calendar.SelectedDate);
        Assert.Equal(picker.DisplayDateStart, calendar.DisplayDateStart);
        Assert.Equal(picker.DisplayDateEnd, calendar.DisplayDateEnd);
        Assert.True(calendar.BlackoutDates.Contains(new DateTime(2026, 6, 10)));
        calendar.SelectedDate = new DateTime(2026, 6, 8);
        Assert.Equal(calendar.SelectedDate, picker.SelectedDate);
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectedDate = new DateTime(2026, 6, 10));
        var header = (Button)item.Template.FindName("PART_HeaderButton", item);
        Assert.Same(Application.Current.FindResource("DefaultTextColor"), header.Foreground);
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(CalendarMode.Year, calendar.DisplayMode);
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(CalendarMode.Decade, calendar.DisplayMode);
    });

    [Fact]
    public Task FailedBookmarkRemovalKeepsTheIconFilterAndVisibleErrorUntilRetry() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        SeedGames(database);
        var view = new GamesView(database.Options);
        var model = (GamesViewModel)view.DataContext;
        var item = model.Games.First();
        item.ToggleBookmarkCommand.Execute(null);
        model.FilterBookmarked(true);
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_remove BEFORE DELETE ON Bookmarks BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        item.ToggleBookmarkCommand.Execute(null);
        await Dispatcher.Yield();
        Assert.True(item.IsBookmarked);
        Assert.Equal(1, model.AvailableGamesCount);
        var error = (TextBlock)view.FindName("BookmarkErrorText");
        Assert.Equal("Unable to save this bookmark. Please try again.", error.Text);
        Assert.Equal(Visibility.Visible, error.Visibility);
        using (var db = database.CreateContext())
        {
            Assert.Single(db.Bookmarks);
            db.Database.ExecuteSqlRaw("DROP TRIGGER reject_remove;");
        }
        item.ToggleBookmarkCommand.Execute(null);
        await Dispatcher.Yield();
        Assert.False(item.IsBookmarked);
        Assert.Equal(0, model.AvailableGamesCount);
        Assert.Equal(Visibility.Collapsed, error.Visibility);
    });

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public Task GameAndSessionTemplatesDisplayUsdRegardlessOfElementLanguage(string language) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        SeedGames(database);
        var gameView = new GamesView(database.Options);
        var model = (GamesViewModel)gameView.DataContext;
        var list = (ListBox)gameView.FindName("GamesList");
        var card = (FrameworkElement)list.ItemTemplate.LoadContent();
        card.Language = XmlLanguage.GetLanguage(language);
        card.DataContext = model.Games.Single(g => g.Game.Name == "Example");
        card.Measure(new Size(1000, 400));
        card.Arrange(new Rect(0, 0, 1000, 400));
        await Dispatcher.Yield();
        Assert.Contains(Descendants<TextBlock>(card), text => text.Text == "$1.50");
        var game = model.Games.Single(g => g.Game.Name == "Example").Game;
        using (var db = database.CreateContext())
        {
            db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name,
                Date = new DateTime(2026, 6, 5), RecordedDate = new DateTime(2026, 6, 5),
                Status = SessionStatus.Verified, Earnings = 1.25m });
            db.SaveChanges();
        }
        gameView.Language = XmlLanguage.GetLanguage(language);
        gameView.OpenGameDetails(game);
        var details = (FrameworkElement)gameView.FindName("GameDetailsView");
        details.Measure(new Size(800, 800));
        details.Arrange(new Rect(0, 0, 800, 800));
        await Dispatcher.Yield();
        Assert.Contains(Descendants<TextBlock>(details), text => text.Text == "$1.50/hr");
        Assert.Contains(Descendants<TextBlock>(details), text => text.Text == "$1.25");
        var sessionView = new SessionsView(); // Loading is not raised, so no default database is opened.
        var sessions = (ListBox)sessionView.FindName("SessionsList");
        var row = (FrameworkElement)sessions.ItemTemplate.LoadContent();
        row.Language = XmlLanguage.GetLanguage(language);
        row.DataContext = new SessionItemViewModel(new Session { Earnings = 1234.5m, Status = SessionStatus.Verified });
        row.Measure(new Size(1000, 400));
        row.Arrange(new Rect(0, 0, 1000, 400));
        await Dispatcher.Yield();
        Assert.Contains(Descendants<TextBlock>(row), text => text.Text == "$1,234.50");
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
