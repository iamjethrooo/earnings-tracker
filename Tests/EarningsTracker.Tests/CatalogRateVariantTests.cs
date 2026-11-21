using System.IO;
using System.Text.Json;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("Catalog updates")]
[Trait("Category", "Regression")]
public class CatalogRateVariantTests
{
    private const string LegacyId = "6cfcc31f-10b6-430d-a828-9a611b75172e";

    private static CatalogSnapshot Bundle() => CatalogService.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));

    private static CatalogSnapshot PreviousBundle()
    {
        var snapshot = Bundle();
        snapshot.Games.RemoveAll(entry => entry.RateVariant != null);
        snapshot.Version = "catalog-before-rate-mapping";
        snapshot.PublishedAt = snapshot.PublishedAt.AddDays(-1);
        // Parse attaches the exact payload; after changing a fixture serialize it again.
        return CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions));
    }

    private static Game Rate(decimal rate) => new() { Name = "Predecessor", RatePerHour = rate, MinutesCap = 3000m };

    [Theory]
    [InlineData(150)]
    [InlineData(200)]
    public void MapsTheOtherRateWithoutMovingTheLegacyLinkOrFinancialHistory(int legacyRateCents)
    {
        using var database = new TestDatabase();
        int legacyGameId;
        int addedGameId;
        using (var db = database.CreateContext())
        {
            var legacy = Rate(legacyRateCents / 100m);
            legacy.CapResetDate = new DateTime(2026, 10, 1);
            db.Sessions.Add(new Session { Game = legacy, GameName = legacy.Name, Earnings = 1.2345m });
            db.SaveChanges();
            legacyGameId = legacy.Id;
            db.Bookmarks.Add(new Bookmark { GameId = legacy.Id });
            db.GameOwnership.Add(new GameOwnership { GameId = legacy.Id, IsOwned = true, PurchaseAmount = 10.50m });
            db.SaveChanges();
            CatalogService.Import(db, PreviousBundle());
            var added = Rate(legacy.RatePerHour == 2m ? 1.5m : 2m);
            db.Sessions.Add(new Session { Game = added, GameName = added.Name, Earnings = 2.3456m });
            db.SaveChanges();
            addedGameId = added.Id;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var db = database.CreateContext();
            var snapshot = Bundle();
            snapshot.Games = snapshot.Games.OrderByDescending(entry => entry.RateVariant != null).ToList();
            CatalogService.Import(db, CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions)));
        }
        using var read = database.CreateContext();
        Assert.Equal(2, read.Games.Count());
        Assert.Equal(legacyGameId, read.GameMetadata.Single(link => link.CatalogId == LegacyId).GameId);
        var addedLink = read.GameMetadata.Single(link => link.GameId == addedGameId);
        var addedTerms = Bundle().Games.Single(entry => entry.CatalogId == addedLink.CatalogId).RateVariant!;
        Assert.Equal(legacyRateCents == 200 ? 1.5m : 2m, addedTerms.RatePerHour);
        Assert.Equal(read.GameMetadata.Single(link => link.GameId == legacyGameId).CoverSha256, addedLink.CoverSha256);
        Assert.True(addedLink.Reviewed);
        Assert.Equal(115078, addedLink.IgdbId);
        Assert.True(addedLink.SupportsMultiplayer);
        Assert.Contains(read.StoreOffers.Where(offer => offer.GameId == addedGameId), offer => offer.Url == "https://store.steampowered.com/app/961200/");
        Assert.Equal(new[] { 1.2345m, 2.3456m }, read.Sessions.OrderBy(session => session.Id).Select(session => session.Earnings).ToArray());
        Assert.Equal(new[] { legacyGameId, addedGameId }, read.Sessions.OrderBy(session => session.Id).Select(session => session.GameId!.Value).ToArray());
        Assert.Equal(new DateTime(2026, 10, 1), read.Games.Single(game => game.Id == legacyGameId).CapResetDate);
        Assert.Equal(legacyGameId, Assert.Single(read.Bookmarks).GameId);
        Assert.Equal(10.50m, Assert.Single(read.GameOwnership).PurchaseAmount);
        Assert.Empty(read.GameChanges);
        Assert.Empty(read.SessionChanges);
    }

    [Fact]
    public void VariantFirstInputStillAssignsTheLegacyIdentityBeforeItsComplement()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var oldRate = Rate(1.5m); var revisedRate = Rate(2m);
        db.Games.AddRange(oldRate, revisedRate); db.SaveChanges();
        var snapshot = Bundle();
        snapshot.Games = snapshot.Games.OrderByDescending(entry => entry.RateVariant != null).ToList();
        CatalogService.Import(db, CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions)));
        Assert.Equal(revisedRate.Id, db.GameMetadata.Single(link => link.CatalogId == LegacyId).GameId);
        var variant = db.GameMetadata.Single(link => link.GameId == oldRate.Id);
        Assert.Equal(1.5m, snapshot.Games.Single(entry => entry.CatalogId == variant.CatalogId).RateVariant!.RatePerHour);
        Assert.Equal(2, db.Games.Count());
    }

    [Fact]
    public void SchemaTwoRatesCarryIndependentReviewedSteamMappingsForTheSameApp()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.AddRange(Rate(2m), Rate(1.5m)); db.SaveChanges();
        var snapshot = Bundle();
        snapshot.SchemaVersion = 2;
        snapshot.Version += "-schema2-fixture";
        snapshot.PublishedAt = DateTime.UtcNow.AddMinutes(-1);
        snapshot.Games = snapshot.Games.Where(entry => entry.CatalogId == LegacyId || entry.RateVariant != null).ToList();
        foreach (var entry in snapshot.Games)
            entry.SteamIdentity = new("Reviewed", snapshot.PublishedAt.ToString("O"), "Reviewed fixture",
                [new CatalogSteamApp(961200, "FullGame")]);
        CatalogService.Import(db, CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions)));
        var identities = CatalogService.ReadSteamIdentities(db)!;
        Assert.Equal(2, db.GameMetadata.Count());
        Assert.All(db.GameMetadata, link =>
        {
            var identity = identities.Identities[link.CatalogId];
            Assert.Equal("Reviewed", identity.State);
            Assert.Equal(961200u, Assert.Single(identity.Apps).AppId);
        });
        Assert.Empty(db.GameChanges);
        Assert.Empty(db.SessionChanges);
    }

    [Fact]
    public void FailedVariantInsertRollsBackMetadataAndVersionWithoutChangingGames()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(Rate(2m), Rate(1.5m)); db.SaveChanges();
            CatalogService.Import(db, PreviousBundle());
            db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectRateMetadata BEFORE INSERT ON GameMetadata BEGIN SELECT RAISE(ABORT, 'test metadata failure'); END;");
            Assert.Throws<DbUpdateException>(() => CatalogService.Import(db, Bundle()));
        }
        using var read = database.CreateContext();
        Assert.Equal("catalog-before-rate-mapping", CatalogService.GetSetting(read, "catalog.version"));
        Assert.Equal(LegacyId, Assert.Single(read.GameMetadata).CatalogId);
        Assert.Equal(2, read.Games.Count());
        Assert.Empty(read.GameChanges);
        Assert.Empty(read.SessionChanges);
    }

    [Fact]
    public void AlreadyActiveSnapshotCanMapANewlyRestoredRateWithoutChangingItsHash()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.Add(Rate(2m)); db.SaveChanges();
        CatalogService.ImportBundled(db);
        var version = CatalogService.GetSetting(db, "catalog.version");
        var hash = CatalogService.GetSetting(db, "catalog.hash." + version);
        db.Games.Add(Rate(1.5m)); db.SaveChanges();
        CatalogService.ImportBundled(db);
        Assert.Equal(2, db.GameMetadata.Count());
        Assert.All(db.GameMetadata, link => Assert.True(link.Reviewed));
        Assert.Equal(hash, CatalogService.GetSetting(db, "catalog.hash." + version));
        Assert.Equal(CatalogService.ActivationStatus.UpToDate,
            CatalogService.Activate(db, Bundle(), TestContext.Current.CancellationToken).Status);
        Assert.Empty(db.GameChanges);
        Assert.Empty(db.SessionChanges);
    }

    [Fact]
    public void AmbiguousAndCustomizedRowsAreNotGuessedAndMissingRowsAreNotCreated()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        CatalogService.ImportBundled(db);
        Assert.Empty(db.Games);
        var duplicateA = Rate(1.5m); var duplicateB = Rate(1.5m); var customized = Rate(2m);
        customized.MinutesCap = 60m;
        db.Games.AddRange(duplicateA, duplicateB, customized); db.SaveChanges();
        var snapshot = Bundle(); snapshot.Version += "-ambiguous-fixture";
        snapshot = CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions));
        CatalogService.Import(db, snapshot);
        Assert.Equal(3, db.Games.Count());
        Assert.Empty(db.GameMetadata);
        Assert.Empty(db.GameChanges);
        Assert.Equal(60m, customized.MinutesCap);
    }

    [Fact]
    public void CatalogRollbackRetainsBothGamesAndTheirStableLinks()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.AddRange(Rate(2m), Rate(1.5m)); db.SaveChanges();
        CatalogService.Import(db, PreviousBundle());
        CatalogService.ImportBundled(db);
        var links = db.GameMetadata.AsNoTracking().ToDictionary(link => link.GameId, link => link.CatalogId);
        var variant = db.GameMetadata.Single(link => link.CatalogId != LegacyId);
        CatalogService.Rollback(db, CatalogService.GetRollbackVersions(db), TestContext.Current.CancellationToken);
        Assert.Equal(2, db.Games.Count());
        Assert.Equal(links.OrderBy(pair => pair.Key), db.GameMetadata.AsNoTracking()
            .ToDictionary(link => link.GameId, link => link.CatalogId).OrderBy(pair => pair.Key));
        Assert.False(db.GameMetadata.Single(link => link.GameId == variant.GameId).Reviewed);
        Assert.Empty(db.GameChanges);
        Assert.Empty(db.SessionChanges);
    }

    [Fact]
    public void InvalidRateVariantContractsAreRejectedBeforeWrites()
    {
        foreach (var invalid in new[]
        {
            new CatalogRateVariant("Predecessor", 0m, 3000, LegacyId),
            new CatalogRateVariant("Predecessor", 1.555m, 3000, LegacyId),
            new CatalogRateVariant("Predecessor", 1.5m, 0, LegacyId),
            new CatalogRateVariant("Predecessor", 1.5m, 3000, "missing-legacy"),
            new CatalogRateVariant("Different game", 1.5m, 3000, LegacyId)
        })
        {
            using var database = new TestDatabase();
            using var db = database.CreateContext();
            var snapshot = Bundle();
            snapshot.Games.First(entry => entry.RateVariant != null).RateVariant = invalid;
            Assert.Throws<InvalidDataException>(() => CatalogService.Import(db, snapshot));
            Assert.Empty(db.Games);
            Assert.Empty(db.GameMetadata);
            Assert.Empty(db.CatalogSettings);
        }
    }
}
