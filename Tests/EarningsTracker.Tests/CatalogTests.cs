using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

[Collection("Catalog updates")]
public class CatalogTests
{
    [Fact]
    public void ResolvedActiveCatalogUsesExactEditionsAndKeepsUnknownAccessUnknown()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        EarningsTracker.Data.DbInitializer.Initialize(db); CatalogService.ImportBundled(db);
        Assert.Equal(579, db.Games.Count());
        var reviewed = db.GameMetadata.ToDictionary(x => x.GameId, x => x.Reviewed);
        Assert.All(db.Games.Where(g => !g.IsArchived), g => Assert.True(reviewed[g.Id]));
        Assert.All(db.SubscriptionAvailability, s => Assert.Equal("PC Game Pass", s.Service));
        var snapshot = CatalogService.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));
        CatalogEntry Entry(string name) => snapshot.Games.Single(g => g.Name == name);
        Assert.Equal(9066, Entry("Cities").IgdbId);
        Assert.Equal(264412, Entry("WRC").IgdbId);
        Assert.Equal(102832, Entry("Baldi's Basics").IgdbId);
        Assert.Equal(190824, Entry("Spaceflight Simulator M").IgdbId);
        foreach (var name in new[] { "Fast Food Simulator: Prologue", "TCG Card Shop Simulator: Prologue", "Sound Space +", "OPTCG Simulator" })
        {
            Assert.True(Entry(name).Reviewed); Assert.Null(Entry(name).IgdbId);
        }
        foreach (var name in new[] { "Coop Deluxe", "eFootball PES 2021", "New World" })
            Assert.All(Entry(name).Offers, o =>
            {
                Assert.Equal(AcquisitionKind.Unknown, o.Kind);
                Assert.Null(o.RegularPrice); Assert.Null(o.SalePrice);
                Assert.False(CatalogPresentation.IsFree(o, DateTime.UtcNow));
            });
    }

    private static CatalogSnapshot Snapshot(string version = "v1") => new()
    {
        Version = version, PublishedAt = DateTime.UtcNow.AddMinutes(-1),
        Games = [new CatalogEntry { CatalogId = "stable-id", Name = "Example", Reviewed = true,
            Genres = ["Action"], Offers = [Offer()], Provenance = "Reviewed fixture" }]
    };
    internal static StoreOffer Offer() => new()
    {
        Source = "Steam", Url = "https://store.steampowered.com/app/123/", Currency = "PHP",
        RegularPrice = 1000, SalePrice = 500, Kind = AcquisitionKind.Purchase,
        VerifiedAt = DateTime.UtcNow.AddHours(-1), Provenance = "IsThereAnyDeal"
    };

    [Fact]
    public void MigrationAndRepeatedImportsPreserveFinancialAndPersonalData()
    {
        using var database = new TestDatabase();
        int id;
        using (var db = database.CreateContext())
        {
            var game = new Game { Name = "Example", RatePerHour = 2m, MinutesCap = 90, CapResetDate = DateTime.Today };
            db.Games.Add(game); db.SaveChanges(); id = game.Id;
            db.Bookmarks.Add(new Bookmark { GameId = id });
            db.Sessions.Add(new Session { GameId = id, GameName = "Example" });
            db.SaveChanges(); db.Database.Migrate();
            db.GameOwnership.Add(new GameOwnership { GameId = id, IsOwned = true, PurchaseAmount = 200, Currency = "PHP" });
            CatalogService.SetSetting(db, "subscription.PC Game Pass", "true"); db.SaveChanges();
            CatalogService.Import(db, Snapshot());
            CatalogService.Import(db, Snapshot());
        }
        using (var db = database.CreateContext())
        {
            var snapshot = Snapshot("v2"); snapshot.PublishedAt = DateTime.UtcNow;
            snapshot.Games[0].Genres = ["RPG"];
            CatalogService.Import(db, snapshot);
        }
        using var reopened = database.CreateContext();
        Assert.Equal(id, reopened.Games.Single().Id);
        Assert.Equal(2m, reopened.Games.Single().RatePerHour);
        Assert.Equal(90, reopened.Games.Single().MinutesCap);
        Assert.Single(reopened.Sessions); Assert.Single(reopened.Bookmarks);
        Assert.Single(reopened.StoreOffers); Assert.Single(reopened.GameMetadata);
        Assert.Equal("RPG", reopened.GameGenres.Single().Name);
        Assert.True(reopened.GameOwnership.Single().IsOwned);
        Assert.Equal(200m, reopened.GameOwnership.Single().PurchaseAmount);
        Assert.Equal("true", CatalogService.GetSetting(reopened, "subscription.PC Game Pass"));
    }

    [Fact]
    public void ZeroPurchasePricesAreFreeWithoutClassifyingUnknownOrSubscriptionsAsFree()
    {
        var now = DateTime.UtcNow;
        var offer = Offer(); offer.SalePrice = 0;
        Assert.True(CatalogPresentation.IsFree(offer, now));
        Assert.Equal("Free-to-play", CatalogPresentation.CompactPrice(offer, now));
        offer.ExpiresAt = now.AddMinutes(-1);
        Assert.False(CatalogPresentation.IsFree(offer, now));
        offer.RegularPrice = 0;
        Assert.True(CatalogPresentation.IsFree(offer, now));
        offer.Kind = AcquisitionKind.Subscription;
        Assert.False(CatalogPresentation.IsFree(offer, now));
        offer.Kind = AcquisitionKind.Unknown; offer.RegularPrice = null; offer.SalePrice = null;
        Assert.False(CatalogPresentation.IsFree(offer, now));
        Assert.Equal("Price unknown", CatalogPresentation.CompactPrice(offer, now));
    }

    [Fact]
    public void CompactPricesKeepStoreDetailsAndMergeDuplicateSteamOffers()
    {
        var item = new EarningsTracker.ViewModels.GameItemViewModel(new Game { Name = "Example" }, false);
        var paid = Offer(); paid.SalePrice = 500;
        item.SetCatalog(null, [], [paid], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Equal("₱500", item.PriceText);
        Assert.Equal("Steam", item.SourceText);
        Assert.Contains("Steam", item.PriceTooltip);
        Assert.DoesNotContain("checked", item.PriceText);
        var free = Offer(); free.Kind = AcquisitionKind.FreeToPlay; free.SalePrice = null; free.RegularPrice = 0;
        item.SetCatalog(null, [], [paid, free], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Equal("Free-to-play", item.PriceText);
        Assert.Contains("Free-to-play", Assert.Single(item.OfferDetails).Text);
        var deluxe = Offer(); deluxe.Edition = "Deluxe";
        item.SetCatalog(null, [], [paid, free, deluxe], [], null, new Dictionary<string, ExchangeRate>());
        Assert.Equal(2, item.OfferDetails.Count);
    }

    [Fact]
    public void ProviderOffsetTimestampsAreImportedAsUtc()
    {
        var snapshot = Snapshot();
        var instant = DateTimeOffset.UtcNow.AddMinutes(-1);
        var json = JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions);
        json = json.Replace(snapshot.Games[0].Offers[0].VerifiedAt.ToString("O"), instant.ToOffset(TimeSpan.FromHours(2)).ToString("O"));
        // Use the provider's actual offset representation regardless of serializer precision.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["Games"]![0]!["Offers"]![0]!["VerifiedAt"] = instant.ToOffset(TimeSpan.FromHours(2)).ToString("O");
        var parsed = CatalogService.Parse(node.ToJsonString());
        Assert.Equal(DateTimeKind.Utc, parsed.Games[0].Offers[0].VerifiedAt.Kind);
        Assert.Equal(instant.UtcDateTime, parsed.Games[0].Offers[0].VerifiedAt);
    }

    [Fact]
    public void BundledCatalogAccountsForEverySeedIncludingDuplicates()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "games.json");
        EarningsTracker.Data.DbInitializer.Initialize(db, seedPath);
        CatalogService.ImportBundled(db);
        Assert.Equal(579, db.Games.Count()); Assert.Equal(579, db.GameMetadata.Count());
        Assert.Equal(579, db.GameMetadata.Select(x => x.CatalogId).Distinct().Count());
        var bundled = CatalogService.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));
        Assert.Equal(bundled.Games.Count(x => x.Reviewed), db.GameMetadata.Count(x => x.Reviewed));
    }

    [Fact]
    public void UnknownAmbiguousAndConflictingMappingsNeverChangeGames()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        db.Games.AddRange(new Game { Name = "Example" }, new Game { Name = "example" }); db.SaveChanges();
        Assert.Single(CatalogService.Import(db, Snapshot()));
        Assert.Empty(db.GameMetadata);
        var invalid = Snapshot("v2"); invalid.Games.Add(new CatalogEntry { CatalogId = "another", Name = "Other", Aliases = ["Example"] });
        Assert.Throws<InvalidDataException>(() => CatalogService.Import(db, invalid));
        Assert.Equal(2, db.Games.Count());
    }

    [Fact]
    public void FailedImportRollsBackEarlierRowsAndVersion()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example" }); db.SaveChanges();
            CatalogService.Import(db, Snapshot());
            db.Database.ExecuteSqlRaw("CREATE TRIGGER fail_offer BEFORE INSERT ON StoreOffers BEGIN SELECT RAISE(ABORT, 'failure'); END;");
            var update = Snapshot("v2"); update.PublishedAt = DateTime.UtcNow; update.Games[0].Genres = ["RPG"];
            Assert.Throws<DbUpdateException>(() => CatalogService.Import(db, update));
        }
        using var reopened = database.CreateContext();
        Assert.Equal("Action", reopened.GameGenres.Single().Name);
        Assert.Single(reopened.StoreOffers);
        Assert.Equal("v1", CatalogService.GetSetting(reopened, "catalog.version"));
    }

    [Fact]
    public void ExpiryCurrencyAndAccessKindsAreDistinct()
    {
        var now = DateTime.UtcNow; var offer = Offer();
        offer.ExpiresAt = now.AddMinutes(-1);
        Assert.Equal(1000m, CatalogPresentation.EffectivePrice(offer, now));
        offer.Kind = AcquisitionKind.Subscription;
        Assert.Null(CatalogPresentation.EffectivePrice(offer, now)); Assert.False(CatalogPresentation.IsFree(offer, now));
        offer.Kind = AcquisitionKind.Giveaway;
        Assert.False(CatalogPresentation.IsFree(offer, now));
        offer.ExpiresAt = now.AddDays(1); Assert.True(CatalogPresentation.IsFree(offer, now));
        offer.Region = "US"; Assert.False(CatalogPresentation.IsFree(offer, now));
        var rates = new Dictionary<string, ExchangeRate> { ["PHP"] = new() { Currency = "PHP", PerUsd = 50, Date = now.Date } };
        Assert.Equal(10m, CatalogPresentation.ToUsd(500, "PHP", rates));
        Assert.Null(CatalogPresentation.ToUsd(500, "EUR", rates));
        var game = new Game { Name = "Example", RatePerHour = 2, MinutesCap = 60 };
        var ownership = new GameOwnership { PurchaseAmount = 500, Currency = "PHP" };
        Assert.Contains("5 hours", CatalogPresentation.BreakEven(game, ownership, rates));
        Assert.Contains("exceeds full-cycle cap", CatalogPresentation.BreakEven(game, ownership, rates));
        game.RatePerHour = 0; Assert.Contains("no hourly", CatalogPresentation.BreakEven(game, ownership, rates));
    }

    [Theory]
    [InlineData("http://example.com/image", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://example.com/image", "bad")]
    public void InvalidArtworkRejected(string url, string hash)
    {
        var snapshot = Snapshot(); snapshot.Games[0].CoverUrl = url; snapshot.Games[0].CoverSha256 = hash;
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
    }

    [Fact]
    public void UnreviewedEnrichmentAndNegativePricesRejected()
    {
        var snapshot = Snapshot(); snapshot.Games[0].Reviewed = false;
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
        snapshot.Games[0].Reviewed = true; snapshot.Games[0].Offers[0].SalePrice = -1;
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
    }

    [Fact]
    public async Task SnapshotFailureKeepsDataAndAutomaticChecksAreThrottled()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" }); db.SaveChanges(); CatalogService.Import(db, Snapshot());
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = new HttpClient(handler);
        var updates = new CatalogUpdates(database.Options, client);
        var message = await updates.RefreshAsync(false);
        Assert.Contains("keeping saved data", message); Assert.Contains("keeping saved rates", message);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("", await updates.RefreshAsync(false)); Assert.Equal(2, handler.Calls);
        Assert.Single(db.StoreOffers);
    }

    [Fact]
    public async Task ValidChecksummedSnapshotAndFxAreApplied()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext()) { db.Games.Add(new Game { Name = "Example" }); db.SaveChanges(); }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Snapshot(), CatalogService.JsonOptions);
        var manifest = JsonSerializer.Serialize(new CatalogManifest { SchemaVersion = 1,
            CatalogUrl = "https://github.com/iamjethrooo/earnings-tracker/releases/download/catalog-latest/catalog.json",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        using var client = new HttpClient(new FakeHandler(request => Ok(request.RequestUri!.AbsolutePath.EndsWith("manifest.json")
            ? manifest : request.RequestUri.Host == "api.frankfurter.dev"
                ? $"[{{\"date\":\"{DateTime.UtcNow:yyyy-MM-dd}\",\"base\":\"USD\",\"quote\":\"PHP\",\"rate\":50}}]"
                : Encoding.UTF8.GetString(bytes))));
        await new CatalogUpdates(database.Options, client).RefreshAsync(false);
        using var reopened = database.CreateContext();
        Assert.Single(reopened.GameMetadata); Assert.Equal(50m, reopened.ExchangeRates.Single().PerUsd);
    }

    [Fact]
    public void PriceResponseKeepsStoreCurrenciesAndWindowsOffersOnly()
    {
        using var json = JsonDocument.Parse("""
        {"deals":[
          {"shop":{"name":"Steam"},"price":{"amount":500,"currency":"PHP"},"regular":{"amount":1000,"currency":"PHP"},"platforms":[{"name":"Windows"}],"timestamp":"2026-01-01T00:00:00Z","expiry":null,"url":"https://example.com/steam?affiliate=keep"},
          {"shop":{"name":"GOG"},"price":{"amount":10,"currency":"USD"},"regular":{"amount":20,"currency":"USD"},"platforms":[{"name":"Windows"}],"timestamp":"2026-01-01T00:00:00Z","expiry":null,"url":"https://example.com/gog"},
          {"shop":{"name":"Mac only"},"platforms":[{"name":"Mac"}]}
        ]}
        """);
        var offers = CatalogUpdates.ParsePrices(json.RootElement, 123, DateTime.UtcNow);
        Assert.Equal(2, offers.Count); Assert.Equal("PHP", offers[0].Currency); Assert.Equal("USD", offers[1].Currency);
        Assert.EndsWith("affiliate=keep", offers[0].Url);
        var rates = new Dictionary<string, ExchangeRate> { ["PHP"] = new() { Currency = "PHP", PerUsd = 50, Date = DateTime.UtcNow.Date } };
        Assert.Contains("≈ ₱500.00", CatalogPresentation.OfferText(offers[1], rates, DateTime.UtcNow));
        Assert.Contains("checked", CatalogPresentation.OfferText(offers[0], rates, DateTime.UtcNow));
        Assert.DoesNotContain("stale", CatalogPresentation.OfferText(offers[0], rates, DateTime.UtcNow));
    }

    [Fact]
    public async Task BadChecksumNeverReplacesSavedCatalog()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" }); db.SaveChanges(); CatalogService.Import(db, Snapshot());
        var manifest = JsonSerializer.Serialize(new CatalogManifest { SchemaVersion = 1,
            CatalogUrl = "https://github.com/iamjethrooo/earnings-tracker/releases/download/test/catalog.json", Sha256 = new string('a', 64) });
        using var client = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("manifest.json")
            ? Ok(manifest) : request.RequestUri.Host == "api.frankfurter.dev" ? new(HttpStatusCode.ServiceUnavailable)
                : Ok(JsonSerializer.Serialize(Snapshot("v2"), CatalogService.JsonOptions))));
        Assert.Contains("keeping saved data", await new CatalogUpdates(database.Options, client).RefreshAsync(false));
        using var reopened = database.CreateContext();
        Assert.Equal("v1", CatalogService.GetSetting(reopened, "catalog.version")); Assert.Single(reopened.StoreOffers);
    }

    [Fact]
    public void SnapshotCannotOverwriteNewerPersonalLivePrices()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" }); db.SaveChanges(); CatalogService.Import(db, Snapshot());
        var offer = db.StoreOffers.Single(); offer.SalePrice = 100;
        CatalogService.SetSetting(db, "prices.live." + offer.GameId, DateTime.UtcNow.ToString("O")); db.SaveChanges();
        CatalogService.Import(db, Snapshot("v2"));
        Assert.Equal(100m, db.StoreOffers.Single().SalePrice);
    }

    [Fact]
    public async Task InvalidKeyDoesNotReplaceSavedPrices()
    {
        using var database = new TestDatabase(); using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" }); db.SaveChanges();
        var snapshot = Snapshot(); snapshot.Games[0].ItadId = Guid.NewGuid().ToString(); CatalogService.Import(db, snapshot);
        using var client = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new CatalogUpdates(database.Options, client).RefreshPricesAsync("invalid-test-key"));
        Assert.Equal(500m, db.StoreOffers.Single().SalePrice);
    }

    internal static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(response(request)); }
    }
}
