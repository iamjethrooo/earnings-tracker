using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;
using EarningsTracker.Views;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
[Trait("Category", "Regression")]
public class GameRosterOrderTests(WpfTestHost host)
{
    [Fact]
    public Task ReviewedNewestOrderSurvivesShuffledIdsAndReopening() => host.RunAsync(() =>
    {
        using var stream = typeof(Game).Assembly.GetManifestResourceStream("EarningsTracker.Data.game-order.json")!;
        var expected = JsonSerializer.Deserialize<List<Game>>(stream)!;
        Assert.Equal(582, expected.Count);
        Assert.DoesNotContain(expected, game => game.Name == "Slay the Spire 2");
        Assert.Equal(new[] { "MAVRIX", "Predecessor", "Wreckreation", "Enlisted", "Scooter Flow" },
            expected.Take(5).Select(game => game.Name));
        Assert.Equal(new[] { "The Long Drive", "Predecessor", "ArcheAge" },
            expected.Skip(381).Take(3).Select(game => game.Name));
        Assert.Equal(1.5m, expected[382].RatePerHour);
        Assert.Equal(2m, expected[1].RatePerHour);

        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            var random = new Random(2383);
            db.Games.AddRange(expected.OrderBy(_ => random.Next()).Select(game => new Game
            {
                Name = game.Name, RatePerHour = game.RatePerHour, MinutesCap = game.MinutesCap, IsArchived = game.IsArchived
            }));
            db.SaveChanges();
        }

        static (string, decimal, decimal, bool) Identity(Game game) =>
            (game.Name, game.RatePerHour, game.MinutesCap, game.IsArchived);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var model = new GamesViewModel(database.Options);
            Assert.Equal(expected.Select(Identity), model.GamesView.Cast<GameItemViewModel>().Select(row => Identity(row.Game)));
            model.ApplySortChoice(1);
            model.ApplySortChoice(2);
            Assert.Equal(expected.Select(Identity), model.GamesView.Cast<GameItemViewModel>().Select(row => Identity(row.Game)));
            model.ShowArchived = false;
            Assert.Equal(expected.Where(game => !game.IsArchived).Select(Identity),
                model.GamesView.Cast<GameItemViewModel>().Select(row => Identity(row.Game)));
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FreshStartupUsesTheReviewedPositionsWithoutChangingSavedArchiveFlags() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            DatabaseInitialization.Initialize(db);
            CatalogService.ImportBundled(db);
        }
        var model = new GamesViewModel(database.Options);
        var ordered = model.GamesView.Cast<GameItemViewModel>().Select(row => row.Game).ToArray();
        Assert.Equal(582, ordered.Length);
        Assert.Equal(new[] { "MAVRIX", "Predecessor", "Wreckreation", "Enlisted", "Scooter Flow" }, ordered.Take(5).Select(game => game.Name));
        Assert.Equal(new[] { "The Long Drive", "Predecessor", "ArcheAge" }, ordered.Skip(381).Take(3).Select(game => game.Name));
        Assert.Equal(1.5m, ordered[382].RatePerHour);
        Assert.Equal(2m, ordered[1].RatePerHour);
        Assert.True(ordered[382].Id > ordered[1].Id);
        Assert.False(ordered.Single(game => game.Name == "Blue Archive").IsArchived);
        Assert.False(ordered.Single(game => game.Name == "Goddess of Victory: Nikke").IsArchived);
        Assert.Contains(ordered, game => game.Name == "Car Mechanic Simulator 2018");
        var firstArchived = Array.FindIndex(ordered, game => game.IsArchived);
        Assert.All(ordered.Skip(firstArchived), game => Assert.True(game.IsArchived));
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnknownGamesAliasesAndOtherSortsKeepTheirExistingBehavior() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Future roster addition", RatePerHour = 3m, MinutesCap = 60m },
                new Game { Name = "MAVRIX", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "Car For Sale Simulator 2023", RatePerHour = 1.5m, MinutesCap = 3000m },
                new Game { Name = "Car Mechanic Simulator 2018", RatePerHour = 1.5m, MinutesCap = 3000m },
                new Game { Name = "Grocery Store Simulator", RatePerHour = 1.5m, MinutesCap = 3000m },
                new Game { Name = "Archived custom game", IsArchived = true, RatePerHour = 5m },
                new Game { Name = "Latest custom game", RatePerHour = 1m });
            db.SaveChanges();
        }
        var model = new GamesViewModel(database.Options);
        Game[] Ordered() => model.GamesView.Cast<GameItemViewModel>().Select(row => row.Game).ToArray();
        Assert.Equal(new[] { "Latest custom game", "Future roster addition", "MAVRIX", "Car For Sale Simulator 2023",
            "Car Mechanic Simulator 2018", "Grocery Store Simulator", "Archived custom game" }, Ordered().Select(game => game.Name));
        model.ApplySortChoice(1);
        Assert.Equal(Ordered().Where(game => !game.IsArchived).OrderBy(game => game.Name).Select(game => game.Id),
            Ordered().Where(game => !game.IsArchived).Select(game => game.Id));
        model.ApplySortChoice(0);
        Assert.Equal(Ordered().Where(game => !game.IsArchived).OrderByDescending(game => game.RatePerHour).Select(game => game.Id),
            Ordered().Where(game => !game.IsArchived).Select(game => game.Id));
        model.SortGames("Game.Id", ListSortDirection.Ascending);
        Assert.Equal("Future roster addition", Ordered()[0].Name);
        Assert.True(Ordered()[^1].IsArchived);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(740)]
    [InlineData(1280)]
    public Task RenderNewestOrderWithDistinctPredecessorRates(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Predecessor", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "Wreckreation", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "The Long Drive", RatePerHour = 1.5m, MinutesCap = 3000m },
                new Game { Name = "ArcheAge", RatePerHour = 1.5m, MinutesCap = 3000m },
                new Game { Name = "MAVRIX", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "Predecessor", RatePerHour = 1.5m, MinutesCap = 3000m });
            db.SaveChanges();
        }
        var view = new GamesView(database.Options) { Width = width, Height = 900 };
        view.Measure(new Size(width, 900)); view.Arrange(new Rect(0, 0, width, 900)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)view.FindName("GamesList");
        Assert.Equal(new[] { "MAVRIX", "Predecessor", "Wreckreation", "The Long Drive", "Predecessor", "ArcheAge" },
            list.Items.Cast<GameItemViewModel>().Select(row => row.Game.Name));
        Assert.Equal(2m, ((GameItemViewModel)list.Items[1]).Game.RatePerHour);
        Assert.Equal(1.5m, ((GameItemViewModel)list.Items[4]).Game.RatePerHour);
        var directory = Path.Combine(AppContext.BaseDirectory, "GameRosterOrderVisuals");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, 900, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"newest-predecessor-{width}.png"));
        encoder.Save(file);
    });
}
