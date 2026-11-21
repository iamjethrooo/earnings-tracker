using System.IO;
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
public class NewGameArtworkTests(WpfTestHost host)
{
    [Theory]
    [InlineData(740)]
    [InlineData(1280)]
    public Task NewGamesAndBothPredecessorRatesHaveReviewedOfflineArtwork(int width) => host.RunAsync(async () =>
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(new Game { Name = "Predecessor", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "MAVRIX", RatePerHour = 2m, MinutesCap = 3000m },
                new Game { Name = "Wreckreation", RatePerHour = 2m, MinutesCap = 3000m });
            db.SaveChanges();
            GameService.ApplyBundledRosterUpdate(db);
            CatalogService.ImportBundled(db);
            Assert.Equal(4, db.GameMetadata.Count());
            foreach (var metadata in db.GameMetadata)
            {
                Assert.True(metadata.Reviewed);
                var path = Path.Combine(AppContext.BaseDirectory, "Data", "Artwork", metadata.CoverSha256 + ".img");
                Assert.True(File.Exists(path));
                Assert.Equal(metadata.CoverSha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
                var bitmap = Assert.IsType<BitmapImage>(await CatalogArtwork.LoadAsync(metadata.CoverUrl, metadata.CoverSha256));
                Assert.True(bitmap.IsFrozen);
                Assert.InRange(bitmap.PixelWidth, 1, 160);
            }
        }
        var view = new GamesView(database.Options) { Width = width, Height = 1050 };
        var model = (GamesViewModel)view.DataContext;
        Assert.All(model.Games, row => Assert.NotNull(row.Cover));
        var predecessors = model.Games.Where(row => row.Game.Name == "Predecessor").ToList();
        Assert.Equal(2, predecessors.Count);
        Assert.Equal(predecessors[0].Metadata!.CoverSha256, predecessors[1].Metadata!.CoverSha256);
        Assert.NotEqual(predecessors[0].Metadata!.CatalogId, predecessors[1].Metadata!.CatalogId);
        Assert.All(predecessors, row => Assert.Contains("MOBA", row.Genres));
        view.Measure(new Size(width, 1050)); view.Arrange(new Rect(0, 0, width, 1050)); view.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var list = (ListBox)view.FindName("GamesList");
        Assert.Equal(new[] { "MAVRIX", "Predecessor", "Wreckreation", "Predecessor" },
            list.Items.Cast<GameItemViewModel>().Select(row => row.Game.Name));
        var directory = Path.Combine(AppContext.BaseDirectory, "NewGameArtworkVisuals");
        Directory.CreateDirectory(directory);
        var bitmapImage = new RenderTargetBitmap(width, 1050, 96, 96, PixelFormats.Pbgra32);
        bitmapImage.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmapImage));
        using var file = File.Create(Path.Combine(directory, $"reviewed-artwork-{width}.png"));
        encoder.Save(file);
    });
}
