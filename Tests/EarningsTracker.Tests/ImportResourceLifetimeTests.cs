using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using EarningsTracker.Data;
using EarningsTracker.Controls;
using EarningsTracker.Models;
using EarningsTracker.Views.Modals;
using System.Windows;
using System.Windows.Controls;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class ImportResourceLifetimeTests(WpfTestHost host)
{
    private static Game SeedGame(TestDatabase database)
    {
        using var db = database.CreateContext();
        var game = new Game { Name = "Example", RatePerHour = 1m, MinutesCap = 60m };
        db.Games.Add(game);
        db.SaveChanges();
        return game;
    }

    private static DbContextOptions<AppDbContext> ObserveDisposal(TestDatabase database, List<string> disposals) =>
        new DbContextOptionsBuilder<AppDbContext>(database.Options)
            .LogTo(disposals.Add, [CoreEventId.ContextDisposed])
            .Options;

    private static UserControl CreateView(bool selectedGame, Game game, DbContextOptions<AppDbContext> options) =>
        selectedGame ? new AddSessionView(game, options) : new ImportSessionsView(options);

    private static void Parse(UserControl view, bool selectedGame)
    {
        ((TextBox)view.FindName("ImportTextBox")).Text = selectedGame
            ? "recording-20260605120000123.mkv\n6/5/2026 · 1m\n+$1.00\nVerified"
            : "Example\nvideo.mkv · 6/5/2026 12:00 PM · 1m\n+$1.00\nVerified";
        Click(view, "Parse Import");
    }

    private static void Click(UserControl view, string content) =>
        Descendants<Button>(view).Single(button => button.Content as string == content)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task SessionCompletion(UserControl view) => view is AddSessionView selected
        ? selected.OperationCompletion : ((ImportSessionsView)view).OperationCompletion;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task CatalogContextIsDisposedAfterPreviewSucceedsOrFails(bool selectedGame, bool failQuery) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = SeedGame(database);
        if (failQuery)
        {
            using var setup = database.CreateContext();
            setup.Database.ExecuteSqlRaw("DROP TABLE Games;");
        }
        var disposals = new List<string>();
        var options = ObserveDisposal(database, disposals);
        var view = CreateView(selectedGame, game, options);
        Assert.Empty(disposals); // Construction does not read the catalog on the UI thread.
        Parse(view, selectedGame);
        await SessionCompletion(view);
        Assert.Equal(failQuery ? 1 : 2, disposals.Count);
        if (failQuery)
            Assert.Equal("Import Failed", ((TextBlock)((ImportResultModal)view.FindName("ImportResultModal")).FindName("ResultTitle")).Text);
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        Assert.Empty(read.SessionChanges);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PreviewAndCancellationReleaseContextsAndDoNotWriteRecords(bool selectedGame) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = SeedGame(database);
        var disposals = new List<string>();
        var view = CreateView(selectedGame, game, ObserveDisposal(database, disposals));
        Parse(view, selectedGame);
        await SessionCompletion(view);
        Assert.Equal(2, disposals.Count); // Catalog and preview each finish their own database operation.
        Click(view, "Back");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        using var read = database.CreateContext();
        Assert.Empty(read.Sessions);
        Assert.Empty(read.SessionChanges);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ImportReleasesContextsAndRetainsAtomicSaveAndRetryBehavior(bool selectedGame, bool failSave) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var game = SeedGame(database);
        var disposals = new List<string>();
        var view = CreateView(selectedGame, game, ObserveDisposal(database, disposals));
        Parse(view, selectedGame);
        await SessionCompletion(view);
        if (failSave)
        {
            using var setup = database.CreateContext();
            setup.Database.ExecuteSqlRaw("CREATE TRIGGER reject_modal_audit BEFORE INSERT ON SessionChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        }
        Click(view, "Import Sessions");
        await SessionCompletion(view);
        Assert.Equal(3, disposals.Count);
        var summary = (ImportResultModal)view.FindName("ImportResultModal");
        if (failSave)
        {
            Assert.Equal("Import Failed", ((TextBlock)summary.FindName("ResultTitle")).Text);
            using (var read = database.CreateContext())
            {
                Assert.Empty(read.Sessions);
                Assert.Empty(read.SessionChanges);
                read.Database.ExecuteSqlRaw("DROP TRIGGER reject_modal_audit;");
            }
            Click(view, "Import Sessions");
            await SessionCompletion(view);
            Assert.Equal(4, disposals.Count);
        }
        Assert.Equal("Import Complete", ((TextBlock)summary.FindName("ResultTitle")).Text);
        using (var read = database.CreateContext())
        {
            var session = Assert.Single(read.Sessions);
            Assert.Equal(1m, session.Earnings);
            Assert.Equal(game.Id, session.GameId);
            Assert.Equal(session.RecordedDate, session.PostedDate);
            Assert.NotNull(Assert.Single(read.SessionChanges).SourceText);
        }
        var completedDisposals = disposals.Count;
        Click(view, "Import Sessions");
        await SessionCompletion(view);
        Assert.Equal(completedDisposals, disposals.Count);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        using var reopened = database.CreateContext();
        Assert.Single(reopened.Sessions);
        Assert.Single(reopened.SessionChanges);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RepeatedDialogConstructionDoesNotOpenDatabaseContexts(bool selectedGame) => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var game = SeedGame(database);
        var disposals = new List<string>();
        var options = ObserveDisposal(database, disposals);
        for (var i = 0; i < 5; i++)
        {
            var view = CreateView(selectedGame, game, options);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        Assert.Empty(disposals);
        return Task.CompletedTask;
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingPayoutModalReleasesItsPreviewContextWithoutDependingOnUnload(bool completeImport) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var disposals = new List<string>();
        var view = new ImportPayoutsView(ObserveDisposal(database, disposals));
        var modal = new Modal { ModalContent = view };
        var closed = 0;
        modal.CloseRequested += (_, _) =>
        {
            Assert.Null(modal.ModalContent);
            Assert.Single(disposals);
            closed++;
        };
        ((TextBox)view.FindName("ImportTextBox")).Text = "|6/5/2026|—|Payment|Example|Paid|-$1.00|";
        Click(view, "Parse Import");
        await view.OperationCompletion;
        Assert.Empty(disposals); // PayoutImporter owns the context through preview and commit.
        if (completeImport)
        {
            Click(view, "Import");
            await view.OperationCompletion;
            Assert.Equal("Done", ((Button)view.FindName("ConfirmImportButton")).Content);
            view.ImportCompleted += (_, _) => modal.Close();
            Click(view, "Done");
        }
        else
        {
            ((Grid)modal.FindName("ModalHeader")).Children.OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert.Equal(1, closed);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        modal.Close();
        Assert.Equal(2, closed);
        Assert.Single(disposals);
        using var read = database.CreateContext();
        Assert.Equal(completeImport ? 1 : 0, read.Payouts.Count());
        Assert.Equal(completeImport ? 1 : 0, read.PayoutChanges.Count());
    });

    [Fact]
    public Task ReplacingPayoutModalContentReleasesThePreviousImporter() => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        var disposals = new List<string>();
        var options = ObserveDisposal(database, disposals);
        var first = new ImportPayoutsView(options);
        var modal = new Modal { ModalContent = first };
        ((TextBox)first.FindName("ImportTextBox")).Text = "|6/5/2026|—|Payment|Example|Paid|-$1.00|";
        Click(first, "Parse Import");
        await first.OperationCompletion;
        modal.ModalContent = new ImportResultModal();
        Assert.Single(disposals);
        first.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        modal.Close();
        Assert.Single(disposals);
        Assert.Null(modal.ModalContent);
        using var read = database.CreateContext();
        Assert.Empty(read.Payouts);
        Assert.Empty(read.PayoutChanges);
    });
}
