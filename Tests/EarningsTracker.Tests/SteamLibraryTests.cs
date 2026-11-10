using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;

namespace EarningsTracker.Tests;

public class SteamLibraryTests
{
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal const string SteamId = "76561198012345678";
    internal const string Key = "0123456789abcdef0123456789abcdef";

    internal sealed class FakeCredentials : ISteamCredentials
    {
        internal string? Key;
        internal bool FailSave;
        internal bool FailRemove;
        public string? Get() => Key;
        public void Save(string key)
        {
            if (FailSave) throw new InvalidOperationException("Synthetic credential failure");
            Key = key;
        }
        public void Remove()
        {
            if (FailRemove) throw new InvalidOperationException("Synthetic credential failure");
            Key = null;
        }
    }

    internal sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => Json("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":220}]}}");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.steampowered.com", request.RequestUri.Host);
            return Task.FromResult(Respond(request));
        }
    }

    internal static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(SteamId, SteamId, null)]
    [InlineData("https://steamcommunity.com/profiles/" + SteamId + "/", SteamId, null)]
    [InlineData("https://steamcommunity.com/id/my-profile_1/", null, "my-profile_1")]
    public void ParsesOnlySupportedProfiles(string input, string? id, string? vanity)
        => Assert.Equal((id, vanity), SteamLibrary.ParseProfile(input));

    [Theory]
    [InlineData("https://steamcommunity.com.evil.test/profiles/" + SteamId)]
    [InlineData("http://steamcommunity.com/profiles/" + SteamId)]
    [InlineData("https://steamcommunity.com@evil.test/id/name")]
    [InlineData("https://steamcommunity.com/id/name?key=secret")]
    [InlineData("https://steamcommunity.com:1234/id/name")]
    [InlineData("https://steamcommunity.com/id/name/games")]
    [InlineData("76561197960265728")]
    [InlineData("not-a-profile")]
    public void RejectsUntrustedProfileInputs(string input)
        => Assert.Throws<ArgumentException>(() => SteamLibrary.ParseProfile(input));

    [Fact]
    public async Task LinkResolvesVanityAndPersistsOnlyIdsWithoutTouchingManualOwnership()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Synthetic game" }); db.SaveChanges();
            db.GameOwnership.Add(new GameOwnership { GameId = db.Games.Single().Id, IsOwned = true,
                Source = "GOG", PurchaseAmount = 12.345m, Currency = "USD" }); db.SaveChanges();
        }
        var handler = new Handler();
        handler.Respond = request => request.RequestUri!.AbsolutePath.Contains("ResolveVanityURL")
            ? Json("{\"response\":{\"success\":1,\"steamid\":\"" + SteamId + "\"}}")
            : Json("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":220,\"name\":\"Do not persist titles\"}]}}");
        using var client = new HttpClient(handler);
        var credentials = new FakeCredentials();
        var service = new SteamLibrary(database.Options, client, credentials);
        await service.LinkAsync("https://steamcommunity.com/id/synthetic", Key, TestContext.Current.CancellationToken);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(Key, credentials.Key);
        using var reopened = database.CreateContext();
        var saved = SteamLibrary.Read(reopened)!;
        Assert.Equal(SteamId, saved.SteamId); Assert.Equal(new HashSet<uint> { 220 }, saved.AppIds);
        Assert.True(saved.SyncedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(reopened.GameOwnership.Single().IsOwned);
        Assert.Equal(12.345m, reopened.GameOwnership.Single().PurchaseAmount);
        Assert.Equal("GOG", reopened.GameOwnership.Single().Source);
        Assert.DoesNotContain(Key, string.Join("", reopened.CatalogSettings.Select(s => s.Value)));
        Assert.DoesNotContain("Do not persist", CatalogService.GetSetting(reopened, SteamLibrary.SnapshotKey));
        var diagnostics = new SettingsViewModel(database.Options).Diagnostics();
        Assert.DoesNotContain(Key, diagnostics); Assert.DoesNotContain(SteamId, diagnostics);
    }

    [Theory]
    [InlineData("{\"response\":{}}")]
    [InlineData("{\"response\":{\"game_count\":2,\"games\":[{\"appid\":220}]}}")]
    [InlineData("{\"response\":{\"game_count\":2,\"games\":[{\"appid\":220},{\"appid\":220}]}}")]
    [InlineData("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":0}]}}")]
    [InlineData("not-json")]
    public async Task InvalidOrPrivateLibraryPreservesSavedEvidenceAndCredential(string payload)
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var credentials = new FakeCredentials(); var service = new SteamLibrary(database.Options, client, credentials);
        await service.LinkAsync(SteamId, Key, TestContext.Current.CancellationToken);
        var before = new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey);
        handler.Respond = _ => Json(payload);
        Assert.Contains("saved library evidence", await service.RefreshAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(before, new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey));
        await Assert.ThrowsAnyAsync<Exception>(() => service.LinkAsync("76561198012345679", new string('a', 32), TestContext.Current.CancellationToken));
        Assert.Equal(before, new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey));
        Assert.Equal(Key, credentials.Key);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task HttpFailuresRetainOwnershipAndNeverPersistSecrets(HttpStatusCode status)
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var service = new SteamLibrary(database.Options, client, new FakeCredentials());
        await service.LinkAsync(SteamId, Key, TestContext.Current.CancellationToken);
        handler.Respond = _ => new(status);
        var result = await service.RefreshAsync(true, TestContext.Current.CancellationToken);
        Assert.Contains("saved library evidence", result); Assert.DoesNotContain(Key, result);
        using var db = database.CreateContext(); Assert.Contains(220u, SteamLibrary.Read(db)!.AppIds);
        Assert.DoesNotContain(Key, string.Join("", db.CatalogSettings.Select(s => s.Value)));
    }

    [Theory]
    [InlineData("delta", 120)]
    [InlineData("date", 180)]
    [InlineData("invalid", 60)]
    public async Task RateLimitPersistsAndBlocksEverySteamRequestUntilRetry(string header, int seconds)
    {
        using var database = new TestDatabase();
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var credentials = new FakeCredentials();
        var clock = new Clock();
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new SteamLibrary(database.Options, client, credentials, clock);
        await service.LinkAsync(SteamId, Key, cancellationToken);
        var settings = new AppSettings(database.Options);
        var saved = settings.Get(SteamLibrary.SnapshotKey);
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (header == "delta") response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            if (header == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(seconds));
            if (header == "invalid") response.Headers.TryAddWithoutValidation("Retry-After", "invalid");
            return response;
        };
        Assert.Contains("rate limited", await service.RefreshAsync(true, cancellationToken));
        var retry = DateTimeOffset.Parse(settings.Get(SteamLibrary.RetryKey), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retry, clock.Now.AddSeconds(seconds - 1), clock.Now.AddSeconds(seconds));
        Assert.Equal(saved, settings.Get(SteamLibrary.SnapshotKey));

        var restarted = new SteamLibrary(database.Options, client, credentials, clock);
        Assert.Contains("rate limited", await restarted.RefreshAsync(false, cancellationToken));
        Assert.Contains("rate limited", await restarted.RefreshAsync(true, cancellationToken));
        await Assert.ThrowsAsync<SteamRateLimitException>(() =>
            restarted.LinkAsync("76561198012345679", Key, cancellationToken));
        Assert.Equal(2, handler.Calls);
        Assert.Equal(saved, settings.Get(SteamLibrary.SnapshotKey));
        using (var db = database.CreateContext())
            Assert.True(SteamLibrary.IsStale(db, SteamLibrary.Read(db)!));
        clock.Now = retry.AddSeconds(1);
        handler.Respond = _ => Json("""{"response":{"game_count":0}}""");
        Assert.Equal("Steam library synced.", await restarted.RefreshAsync(true, cancellationToken));
        Assert.Equal(3, handler.Calls);
        Assert.Empty(settings.Get(SteamLibrary.RetryKey));
    }

    [Fact]
    public async Task UnreadableSavedLibraryLeavesSettingsUsableAndCanBeUnlinked()
    {
        using var database = new TestDatabase();
        var settings = new AppSettings(database.Options);
        settings.Set(SteamLibrary.SnapshotKey, "invalid-json");
        var model = new SettingsViewModel(database.Options);
        Assert.True(model.SteamLinked);
        Assert.Contains("evidence unavailable", model.SteamStatus);
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var service = new SteamLibrary(database.Options, client, new FakeCredentials());
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Contains("Relink or unlink", await service.RefreshAsync(true, cancellationToken));
        await service.UnlinkAsync(cancellationToken);
        Assert.Empty(settings.Get(SteamLibrary.SnapshotKey));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AutomaticSyncIsDailyAndOptionalWhileManualEmptyLibraryClearsOnlySteamEvidence()
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var service = new SteamLibrary(database.Options, client, new FakeCredentials());
        await service.LinkAsync(SteamId, Key, TestContext.Current.CancellationToken);
        Assert.Equal("", await service.RefreshAsync(false, TestContext.Current.CancellationToken)); Assert.Equal(1, handler.Calls);
        var settings = new AppSettings(database.Options);
        settings.Set("steam.lastAttempt", DateTime.UtcNow.AddDays(-2).ToString("O"));
        settings.Set("steam.automatic", "false");
        Assert.Equal("", await service.RefreshAsync(false, TestContext.Current.CancellationToken)); Assert.Equal(1, handler.Calls);
        settings.Set("steam.automatic", "true");
        Assert.Equal("Steam library synced.", await service.RefreshAsync(false, TestContext.Current.CancellationToken)); Assert.Equal(2, handler.Calls);
        handler.Respond = _ => Json("{\"response\":{\"game_count\":0}}");
        await service.RefreshAsync(true, TestContext.Current.CancellationToken);
        using var db = database.CreateContext(); Assert.Empty(SteamLibrary.Read(db)!.AppIds);
    }

    [Fact]
    public async Task FailedCredentialSaveRollsBackAccountReplacementAndUnlinkFailureKeepsLink()
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var credentials = new FakeCredentials(); var service = new SteamLibrary(database.Options, client, credentials);
        await service.LinkAsync(SteamId, Key, TestContext.Current.CancellationToken);
        var before = new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey);
        credentials.FailSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync("76561198012345679", new string('a', 32), TestContext.Current.CancellationToken));
        Assert.Equal(before, new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey)); Assert.Equal(Key, credentials.Key);
        credentials.FailRemove = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnlinkAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before, new AppSettings(database.Options).Get(SteamLibrary.SnapshotKey));
        credentials.FailRemove = false;
        await service.UnlinkAsync(TestContext.Current.CancellationToken);
        using var db = database.CreateContext(); Assert.Null(SteamLibrary.Read(db)); Assert.Null(credentials.Key);
        Assert.Equal("", await service.RefreshAsync(true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuccessfulReplacementUsesOnlyNewLibraryAndMissingKeyKeepsItCached()
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var credentials = new FakeCredentials(); var service = new SteamLibrary(database.Options, client, credentials);
        await service.LinkAsync(SteamId, Key, TestContext.Current.CancellationToken);
        handler.Respond = _ => Json("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":221}]}}");
        await service.LinkAsync("76561198012345679", new string('a', 32), TestContext.Current.CancellationToken);
        using (var db = database.CreateContext())
        {
            var saved = SteamLibrary.Read(db)!;
            Assert.Equal("76561198012345679", saved.SteamId);
            Assert.Equal(new HashSet<uint> { 221 }, saved.AppIds);
        }
        credentials.Key = null;
        Assert.Contains("saved library evidence", await service.RefreshAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Calls);
        using var reopened = database.CreateContext();
        Assert.Equal(new HashSet<uint> { 221 }, SteamLibrary.Read(reopened)!.AppIds);
    }

    [Fact]
    public async Task CancelledOrInvalidLinkNeverWrites()
    {
        using var database = new TestDatabase();
        var handler = new Handler(); using var client = new HttpClient(handler);
        var credentials = new FakeCredentials(); var service = new SteamLibrary(database.Options, client, credentials);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinkAsync(SteamId, "invalid-key", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinkAsync("bad-profile", Key, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.LinkAsync(SteamId, Key, cancellation.Token));
        using var db = database.CreateContext(); Assert.Null(SteamLibrary.Read(db)); Assert.Null(credentials.Key);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void MatchingRequiresReviewedUnambiguousAppIdentityAndNeverUsesTitlesPackagesOrEditions()
    {
        var library = new SteamLibrarySnapshot(SteamId, DateTime.UtcNow, [220]);
        var metadata = new GameMetadata { Reviewed = true };
        var offer = new StoreOffer { Source = "Steam", Edition = "Base game", Url = "https://store.steampowered.com/app/220/" };
        Assert.True(SteamGameMatching.IsOwned(metadata, [offer], library));
        metadata.Reviewed = false; Assert.False(SteamGameMatching.IsOwned(metadata, [offer], library));
        metadata.Reviewed = true;
        offer.Edition = "Reviewed edition"; Assert.False(SteamGameMatching.IsOwned(metadata, [offer], library));
        offer.Edition = "Base game";
        offer.Url = "https://store.steampowered.com/sub/220/"; Assert.False(SteamGameMatching.IsOwned(metadata, [offer], library));
        offer.Url = "https://store.steampowered.com.evil.test/app/220/"; Assert.False(SteamGameMatching.IsOwned(metadata, [offer], library));
        offer.Url = "https://store.steampowered.com/app/221/"; Assert.False(SteamGameMatching.IsOwned(metadata, [offer], library));
        Assert.False(SteamGameMatching.IsOwned(metadata, [], library));
        offer.Url = "https://store.steampowered.com/app/220/";
        Assert.False(SteamGameMatching.IsOwned(metadata, [offer, new StoreOffer { Source = "Steam", Url = "https://store.steampowered.com/app/221/" }], library));
        // Bundled provider identities match even when a price link is an ITAD redirect.
        metadata.ItadId = "018d937e-e9db-717a-baeb-62a6513d146c";
        Assert.True(SteamGameMatching.IsOwned(metadata, [], new(SteamId, DateTime.UtcNow, [220240])));
        metadata.ItadId = "018d937e-f48c-7289-8516-c7c5b2e12eba";
        Assert.False(SteamGameMatching.IsOwned(metadata, [], new(SteamId, DateTime.UtcNow, [49520, 217490])));
    }
}
