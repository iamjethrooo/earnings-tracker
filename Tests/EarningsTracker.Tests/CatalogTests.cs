using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    [Fact]
    public void CatalogRollbackSurvivesRestartAndKeepsPostUpdateFinancialEdits()
    {
        var directory = Directory.CreateTempSubdirectory("earnings-catalog-");
        try
        {
            var token = TestContext.Current.CancellationToken;
            var connection = $"Data Source={Path.Combine(directory.FullName, "profile.db")};Pooling=False;Default Timeout=1";
            var options = new DbContextOptionsBuilder<EarningsTracker.Data.AppDbContext>().UseSqlite(connection).Options;
            var first = Snapshot("catalog-a");
            var next = Snapshot("catalog-b");
            next.PublishedAt = first.PublishedAt.AddSeconds(1);
            next.SchemaVersion = 2;
            next.Games[0].SteamIdentity = new("Unresolved", null, "Pending review", []);
            next.Games[0].Genres = ["Simulation"];
            CatalogService.RollbackVersions confirmed;
            string sessionFingerprint;
            string auditFingerprint;
            using (var db = new EarningsTracker.Data.AppDbContext(options))
            {
                db.Database.Migrate();
                Assert.Throws<InvalidDataException>(() => CatalogService.GetRollbackVersions(db));
                db.Games.Add(new Game { Name = "Example" });
                db.SaveChanges();
                CatalogService.Activate(db, first, token);
                CatalogService.Activate(db, next, token);
                var session = new Session { GameName = "Example", GameId = db.Games.Single().Id, Earnings = 1m };
                db.Sessions.Add(session);
                db.SaveChanges();
                SessionService.Update(session.Id, new Session
                {
                    RecordedDate = DateTime.Today, Duration = TimeSpan.FromMinutes(20), Earnings = 1.234m
                }, options);
                sessionFingerprint = SessionService.Snapshot(db.Sessions.AsNoTracking().Single());
                auditFingerprint = JsonSerializer.Serialize(db.SessionChanges.Single());
                confirmed = CatalogService.GetRollbackVersions(db);
                Assert.Equal(new CatalogService.RollbackVersions("catalog-b", "catalog-a"), confirmed);
            }
            using (var db = new EarningsTracker.Data.AppDbContext(options))
            {
                var previous = CatalogService.GetSetting(db, "catalog.previous-payload");
                CatalogService.SetSetting(db, "catalog.previous-payload", previous.Replace("catalog-a", "catalog-z"));
                db.SaveChanges();
                Assert.Throws<InvalidDataException>(() => CatalogService.Rollback(db, confirmed, token));
                Assert.Equal("catalog-b", CatalogService.GetSetting(db, "catalog.version"));
                using var retained = JsonDocument.Parse(previous);
                var payload = retained.RootElement.GetProperty("Payload").GetString()!
                    .Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");
                CatalogService.SetSetting(db, "catalog.previous-payload", JsonSerializer.Serialize(new
                {
                    SchemaVersion = 99, Version = "catalog-a", Payload = payload,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
                }));
                db.SaveChanges();
                Assert.Throws<InvalidDataException>(() => CatalogService.Rollback(db, confirmed, token));
                CatalogService.SetSetting(db, "catalog.previous-payload", previous);
                db.SaveChanges();
                Assert.Throws<InvalidDataException>(() => CatalogService.Rollback(db, confirmed with { Target = "stale-target" }, token));
                db.Database.ExecuteSqlRaw("""
                    CREATE TRIGGER fail_catalog_restore BEFORE INSERT ON GameGenres
                    WHEN NEW.Name = 'Action' BEGIN SELECT RAISE(ABORT, 'synthetic write failure'); END;
                    """);
                Assert.Throws<DbUpdateException>(() => CatalogService.Rollback(db, confirmed, token));
            }
            using (var db = new EarningsTracker.Data.AppDbContext(options))
            {
                Assert.Equal("catalog-b", CatalogService.GetSetting(db, "catalog.version"));
                Assert.Equal("Simulation", db.GameGenres.Single().Name);
                Assert.Empty(CatalogService.GetSetting(db, "catalog.rejected.catalog-b"));
                db.Database.ExecuteSqlRaw("DROP TRIGGER fail_catalog_restore");
                using var competingWriter = db.Database.BeginTransaction();
                using var blocked = new EarningsTracker.Data.AppDbContext(options);
                Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => CatalogService.Rollback(blocked, confirmed, token));
            }
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var interruptedOptions = new DbContextOptionsBuilder<EarningsTracker.Data.AppDbContext>().UseSqlite(connection)
                    .AddInterceptors(new InterruptCatalogWrite(cancellation)).Options;
                using var interrupted = new EarningsTracker.Data.AppDbContext(interruptedOptions);
                Assert.Throws<OperationCanceledException>(() => CatalogService.Rollback(interrupted, confirmed, cancellation.Token));
            }
            using (var db = new EarningsTracker.Data.AppDbContext(options))
            {
                Assert.Equal("catalog-b", CatalogService.GetSetting(db, "catalog.version"));
                Assert.Equal("Simulation", db.GameGenres.Single().Name);
                Assert.Empty(CatalogService.GetSetting(db, "catalog.rejected.catalog-b"));
                CatalogService.Rollback(db, confirmed, token);
            }
            using (var db = new EarningsTracker.Data.AppDbContext(options))
            {
                Assert.Equal(1.234m, db.Sessions.Single().Earnings);
                Assert.Equal(sessionFingerprint, SessionService.Snapshot(db.Sessions.AsNoTracking().Single()));
                Assert.Equal(auditFingerprint, JsonSerializer.Serialize(db.SessionChanges.Single()));
                Assert.Equal("Action", db.GameGenres.Single().Name);
                Assert.Equal("stable-id", db.GameMetadata.Single().CatalogId);
                Assert.Equal("catalog-a", CatalogService.GetSetting(db, "catalog.version"));
                Assert.Null(CatalogService.ReadSteamIdentities(db));
                Assert.Throws<InvalidDataException>(() => CatalogService.GetRollbackVersions(db));
                Assert.Equal(CatalogService.ActivationStatus.Rejected, CatalogService.Activate(db, next, token).Status);
                var replacement = Snapshot("catalog-c");
                replacement.PublishedAt = next.PublishedAt.AddSeconds(1);
                Assert.Equal(CatalogService.ActivationStatus.Applied, CatalogService.Activate(db, replacement, token).Status);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class InterruptCatalogWrite(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            cancellation.Cancel();
            return result;
        }
    }
    internal static StoreOffer Offer() => new()
    {
        Source = "Steam", Url = "https://store.steampowered.com/app/123/", Currency = "PHP",
        RegularPrice = 1000, SalePrice = 500, Kind = AcquisitionKind.Purchase,
        VerifiedAt = DateTime.UtcNow.AddHours(-1), Provenance = "IsThereAnyDeal"
    };

    [Fact]
    public void SteamIdentityValidationMatchesSharedFixtures()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "steam-identities.json")));
        var root = fixtures.RootElement;
        foreach (var test in root.GetProperty("Cases").EnumerateArray())
        {
            var payload = JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, Version = "fixture", PublishedAt = root.GetProperty("PublishedAt"),
                Games = new[] { new { CatalogId = "a", Name = "Example",
                    Reviewed = !test.TryGetProperty("Reviewed", out var reviewed) || reviewed.GetBoolean(),
                    SteamIdentity = new { State = "Reviewed", ReviewedAt = root.GetProperty("ReviewedAt"),
                        Provenance = "Synthetic review", Apps = test.GetProperty("Apps") } } }
            });
            var failure = Record.Exception(() => CatalogService.Parse(payload));
            Assert.True(test.GetProperty("Valid").GetBoolean() ? failure == null : failure is InvalidDataException or JsonException,
                $"{test.GetProperty("Name").GetString()}: {failure}");
        }
    }

    [Fact]
    public void SchemaTwoPersistsReviewedLinksAndWithdrawalsWithoutRewritingManualOwnership()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var game = new Game { Name = "Example" };
        db.Games.Add(game);
        db.SaveChanges();
        db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true, PurchaseAmount = 75m });
        db.SaveChanges();
        CatalogService.Import(db, Snapshot());

        var snapshot = Snapshot("schema-two");
        snapshot.SchemaVersion = 2;
        var reviewTime = "2026-01-01T00:00:00Z";
        snapshot.Games[0].SteamIdentity = new("Reviewed", reviewTime, "Explicit review", [new(456, "FullGame")]);
        snapshot.Games.Add(new() { CatalogId = "unlinked", Name = "Catalog only", SteamIdentity = new("Unresolved", null, "No local match", []) });
        CatalogSnapshot Parse(CatalogSnapshot input) => CatalogService.Parse(JsonSerializer.Serialize(input, CatalogService.JsonOptions));
        var parsed = Parse(snapshot);
        Assert.Single(CatalogService.Import(db, parsed));
        Assert.Empty(CatalogService.Import(db, parsed));
        using var reopened = database.CreateContext();
        var metadata = reopened.GameMetadata.Single();
        var active = Assert.IsType<CatalogService.SteamIdentityDocument>(CatalogService.ReadSteamIdentities(reopened));
        Assert.Equal(parsed.PayloadHash, active.Sha256);
        Assert.Equal("Explicit review", active.Identities[metadata.CatalogId].Provenance);
        Assert.Contains("unlinked", active.Identities.Keys);
        var library = new SteamLibrarySnapshot("76561197960265729", DateTime.UtcNow, [123, 456]);
        Assert.True(SteamGameMatching.IsOwned(metadata, [], library, active));
        Assert.False(SteamGameMatching.IsOwned(metadata, [Offer()], library with { AppIds = [123] }, active));

        snapshot.Games[0].SteamIdentity = new("Withdrawn", reviewTime, "Corrected", []);
        Assert.Throws<InvalidDataException>(() => CatalogService.Import(db, Parse(snapshot)));
        snapshot.Version = "withdrawal";
        CatalogService.Import(db, Parse(snapshot));
        CatalogService.Import(db, Snapshot("late-schema-one"));
        using var after = database.CreateContext();
        var withdrawn = Assert.IsType<CatalogService.SteamIdentityDocument>(CatalogService.ReadSteamIdentities(after));
        Assert.Equal("withdrawal", withdrawn.Version);
        Assert.False(SteamGameMatching.IsOwned(after.GameMetadata.Single(), [Offer()], library, withdrawn));
        Assert.Equal(game.Id, after.GameMetadata.Single().GameId);
        Assert.Single(after.Games);
        var manual = after.GameOwnership.Single();
        Assert.True(manual.IsOwned);
        Assert.Equal(75m, manual.PurchaseAmount);

        var conflict = Parse(snapshot);
        conflict.Version = "conflict";
        conflict.PublishedAt = conflict.PublishedAt.AddSeconds(1);
        conflict.Games[0].Summary = "Must roll back";
        conflict.Games[1].Aliases = ["Example"];
        conflict.Games[0].Name = "Renamed catalog entry";
        Assert.Throws<InvalidDataException>(() => CatalogService.Import(db, Parse(conflict)));
        using var afterConflict = database.CreateContext();
        Assert.Equal("withdrawal", CatalogService.ReadSteamIdentities(afterConflict)?.Version);
        Assert.Equal("", afterConflict.GameMetadata.Single().Summary);
    }

    [Fact]
    public void MultiplayerMetadataRequiresEvidenceRoundTripsAndLegacyCatalogsStayCompatible()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" }); db.SaveChanges();
        var snapshot = Snapshot();
        var entry = snapshot.Games[0];
        entry.SupportsMultiplayer = true;
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
        entry.MultiplayerProvenance = "Synthetic structured modes; https://www.igdb.com/games/example";
        entry.MultiplayerVerifiedAt = DateTime.UtcNow.AddHours(-1);
        var parsed = CatalogService.Parse(JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions));
        CatalogService.Import(db, parsed);
        CatalogService.Import(db, parsed);
        var saved = db.GameMetadata.Single();
        Assert.True(saved.SupportsMultiplayer);
        Assert.Equal(entry.MultiplayerProvenance, saved.MultiplayerProvenance);
        Assert.Equal(entry.MultiplayerVerifiedAt, saved.MultiplayerVerifiedAt);
        entry.Reviewed = false;
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
        entry.Reviewed = true;
        entry.MultiplayerVerifiedAt = DateTime.UtcNow.AddDays(1);
        Assert.Throws<InvalidDataException>(() => CatalogService.Validate(snapshot));
        var legacy = new { SchemaVersion = 1, Version = "legacy-no-modes", PublishedAt = DateTime.UtcNow,
            Games = new[] { new { CatalogId = "stable-id", Name = "Example", Reviewed = true } } };
        CatalogService.Import(db, CatalogService.Parse(JsonSerializer.Serialize(legacy)));
        Assert.Null(saved.SupportsMultiplayer);
        Assert.Equal("", saved.MultiplayerProvenance);
        Assert.Null(saved.MultiplayerVerifiedAt);
    }

    [Fact]
    public void BundledMultiplayerEvidenceIsStructuredAndDoesNotGuessUnknownGames()
    {
        var snapshot = CatalogService.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json")));
        Assert.Contains(snapshot.Games, entry => entry.SupportsMultiplayer == true);
        Assert.Contains(snapshot.Games, entry => entry.SupportsMultiplayer == false);
        Assert.Contains(snapshot.Games, entry => entry.SupportsMultiplayer == null);
        Assert.All(snapshot.Games.Where(entry => entry.SupportsMultiplayer.HasValue), entry =>
        {
            Assert.True(entry.Reviewed);
            Assert.Contains("https://www.igdb.com/games/", entry.MultiplayerProvenance);
            Assert.NotNull(entry.MultiplayerVerifiedAt);
        });
    }

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
            var initial = Snapshot();
            CatalogService.Import(db, initial);
            CatalogService.Import(db, initial);
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
        Assert.Empty(CatalogService.GetSetting(reopened, "catalog.previous-payload"));
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
        var message = await updates.RefreshAsync(false, TestContext.Current.CancellationToken);
        Assert.Contains("keeping saved data", message); Assert.Contains("keeping saved rates", message);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("", await updates.RefreshAsync(false, TestContext.Current.CancellationToken)); Assert.Equal(2, handler.Calls);
        Assert.Single(db.StoreOffers);
    }

    [Fact]
    public async Task ValidChecksummedSnapshotAndFxAreApplied()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext()) { db.Games.Add(new Game { Name = "Example" }); db.SaveChanges(); }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Snapshot("catalog-test"), CatalogService.JsonOptions);
        var manifest = JsonSerializer.Serialize(new CatalogManifest { SchemaVersion = 1,
            CatalogUrl = "https://github.com/iamjethrooo/earnings-tracker/releases/download/catalog-test/catalog.json",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        using var client = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsoluteUri == CatalogUpdates.SchemaTwoManifestUrl
            ? new(HttpStatusCode.NotFound) : Ok(request.RequestUri.AbsolutePath.EndsWith("manifest.json")
            ? manifest : request.RequestUri.Host == "api.frankfurter.dev"
                ? $"[{{\"date\":\"{DateTime.UtcNow:yyyy-MM-dd}\",\"base\":\"USD\",\"quote\":\"PHP\",\"rate\":50}}]"
                : Encoding.UTF8.GetString(bytes))));
        await new CatalogUpdates(database.Options, client).RefreshAsync(false, TestContext.Current.CancellationToken);
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
            CatalogUrl = "https://github.com/iamjethrooo/earnings-tracker/releases/download/catalog-test/catalog.json", Sha256 = new string('a', 64) });
        using var client = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsoluteUri == CatalogUpdates.SchemaTwoManifestUrl
            ? new(HttpStatusCode.NotFound) : request.RequestUri.AbsolutePath.EndsWith("manifest.json")
            ? Ok(manifest) : request.RequestUri.Host == "api.frankfurter.dev" ? new(HttpStatusCode.ServiceUnavailable)
                : Ok(JsonSerializer.Serialize(Snapshot("v2"), CatalogService.JsonOptions))));
        Assert.Contains("keeping saved data", await new CatalogUpdates(database.Options, client).RefreshAsync(false, TestContext.Current.CancellationToken));
        using var reopened = database.CreateContext();
        Assert.Equal("v1", CatalogService.GetSetting(reopened, "catalog.version")); Assert.Single(reopened.StoreOffers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SchemaTwoCannotReplaceTheCatalogThroughTheSchemaOneFeed(int manifestSchema)
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Example" });
        db.SaveChanges();
        CatalogService.Import(db, Snapshot());
        var incoming = Snapshot("catalog-test");
        incoming.SchemaVersion = 2;
        incoming.Games[0].SteamIdentity = new("Unresolved", null, "No mapping", []);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(incoming, CatalogService.JsonOptions);
        var manifest = JsonSerializer.Serialize(new CatalogManifest { SchemaVersion = manifestSchema,
            CatalogUrl = "https://github.com/iamjethrooo/earnings-tracker/releases/download/catalog-test/catalog.json",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri == CatalogUpdates.SchemaTwoManifestUrl
            ? new(HttpStatusCode.NotFound) : request.RequestUri.AbsolutePath.EndsWith("manifest.json")
            ? Ok(manifest) : request.RequestUri.Host == "api.frankfurter.dev" ? new(HttpStatusCode.ServiceUnavailable)
                : Ok(Encoding.UTF8.GetString(bytes)));
        using var client = new HttpClient(handler);
        Assert.Contains("keeping saved data", await new CatalogUpdates(database.Options, client).RefreshAsync(false, TestContext.Current.CancellationToken));
        Assert.Equal(manifestSchema == 2 ? 3 : 4, handler.Calls);
        using var reopened = database.CreateContext();
        Assert.Equal("v1", CatalogService.GetSetting(reopened, "catalog.version"));
        Assert.Null(CatalogService.ReadSteamIdentities(reopened));
    }

    [Fact]
    public async Task SchemaTwoChecksClientAndAppliesOnlyTheBoundPayload()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Example" });
            db.SaveChanges();
            CatalogService.Import(db, Snapshot("catalog-initial"));
        }
        var snapshot = Snapshot("catalog-next");
        snapshot.PublishedAt = DateTime.UtcNow;
        snapshot.SchemaVersion = 2;
        snapshot.Games[0].SteamIdentity = new("Unresolved", null, "Pending review", []);
        snapshot.Games.Add(new() { CatalogId = "unmatched", Name = "New catalog title",
            SteamIdentity = new("Unresolved", null, "Pending review", []) });
        var asset = "https://github.com/iamjethrooo/earnings-tracker/releases/download/catalog-next/catalog.json";
        var manifest = new CatalogManifest
        {
            SchemaVersion = 2, Version = "catalog-next", CatalogUrl = asset, MinimumClientVersion = "2.0.0"
        };
        var settings = new AppSettings(database.Options);
        var payloadRequests = 0;
        async Task<string> Refresh(Version clientVersion)
        {
            settings.Set("updates.lastAttempt", "");
            var payload = JsonSerializer.Serialize(snapshot, CatalogService.JsonOptions);
            manifest.Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            using var client = new HttpClient(new FakeHandler(request =>
            {
                var uri = request.RequestUri!.AbsoluteUri;
                if (uri == CatalogUpdates.SchemaTwoManifestUrl) return Ok(JsonSerializer.Serialize(manifest));
                if (uri != asset) return new(HttpStatusCode.ServiceUnavailable);
                payloadRequests++;
                return Ok(payload);
            }));
            return await new CatalogUpdates(database.Options, client, clientVersion)
                .RefreshAsync(false, TestContext.Current.CancellationToken);
        }

        Assert.Contains("requires app version 2.0.0", await Refresh(new(1, 9, 0)));
        Assert.Equal(0, payloadRequests);
        Assert.Empty(settings.Get("updates.catalogSuccess"));
        snapshot.Version = "catalog-wrong";
        Assert.Contains("rejected", await Refresh(new(2, 0, 0)));
        Assert.Equal("catalog-initial", settings.Get("catalog.version"));
        snapshot.Version = "catalog-next";
        Assert.Contains("applied · 1 unmatched", await Refresh(new(2, 0, 0)));
        Assert.Contains("is up to date", await Refresh(new(2, 0, 0)));
        snapshot.Games[0].Summary = "Changed bytes under the same version";
        Assert.Contains("rejected", await Refresh(new(2, 0, 0)));
        using var reopened = database.CreateContext();
        Assert.Equal("catalog-next", settings.Get("catalog.version"));
        Assert.Contains("catalog-initial", settings.Get("catalog.previous-payload"));
        Assert.Contains("unmatched", CatalogService.ReadSteamIdentities(reopened)!.Identities.Keys);
        Assert.Empty(reopened.GameMetadata.Single().Summary);
    }

    [Fact]
    public async Task CatalogRejectsRedirectOutsideTrustedAssetHosts()
    {
        using var database = new TestDatabase();
        var handler = new FakeHandler(request => request.RequestUri!.AbsoluteUri == CatalogUpdates.SchemaTwoManifestUrl
            ? new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://untrusted.example/manifest.json") } }
            : new(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var result = await new CatalogUpdates(database.Options, client)
            .RefreshAsync(false, TestContext.Current.CancellationToken);
        Assert.Contains("Catalog update rejected", result);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void OmittedEntryClearsEnrichmentAndKeepsItsStableLink()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var game = new Game { Name = "Example" };
        db.Games.Add(game);
        db.SaveChanges();
        CatalogService.Import(db, Snapshot("catalog-initial"));
        var removal = Snapshot("catalog-removal");
        removal.PublishedAt = DateTime.UtcNow.AddSeconds(-10);
        removal.Games.Clear();
        CatalogService.Import(db, removal);
        Assert.Empty(db.StoreOffers);
        Assert.Empty(db.GameGenres);
        Assert.Equal(game.Id, db.GameMetadata.Single().GameId);
        Assert.False(db.GameMetadata.Single().Reviewed);

        var reintroduced = Snapshot("catalog-restored");
        reintroduced.PublishedAt = removal.PublishedAt.AddSeconds(1);
        reintroduced.Games[0].Name = "New catalog name";
        Assert.Empty(CatalogService.Import(db, reintroduced));
        Assert.Equal(game.Id, db.GameMetadata.Single().GameId);
        Assert.Single(db.StoreOffers);
        Assert.Equal("Example", db.Games.Single().Name);
    }

    [Theory]
    [InlineData("""{"Version":"catalog-test","PublishedAt":"2026-01-01T00:00:00Z","Games":[]}""")]
    [InlineData("""{"SchemaVersion":1,"Version":"catalog-test","PublishedAt":"2026-01-01","Games":[]}""")]
    [InlineData("""{"SchemaVersion":1,"Version":"catalog-test","PublishedAt":"2999-01-01T00:00:00Z","Games":[]}""")]
    public void InvalidSnapshotHeadersAreRejected(string payload)
    {
        Assert.True(Record.Exception(() => CatalogService.Parse(payload)) is JsonException or InvalidDataException);
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
