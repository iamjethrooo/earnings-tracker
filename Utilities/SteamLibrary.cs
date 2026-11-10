using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using EarningsTracker.Data;
using EarningsTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Utilities;

internal sealed record SteamLibrarySnapshot(string SteamId, DateTime SyncedAtUtc, HashSet<uint> AppIds);
internal sealed class SteamRateLimitException(DateTimeOffset retryAt) : Exception(
    $"Steam rate limited requests. Retry after {retryAt.ToLocalTime():g}.")
{
    internal DateTimeOffset RetryAt { get; } = retryAt;
}

internal interface ISteamCredentials
{
    string? Get();
    void Save(string key);
    void Remove();
}

internal sealed class SteamCredentials : ISteamCredentials
{
    private const string Target = "EarningsTracker/Steam";
    public string? Get() => CatalogCredentials.Get(Target);
    public void Save(string key) => CatalogCredentials.Save(key, Target);
    public void Remove() => CatalogCredentials.Remove(Target);
}

// Steam evidence is separate from manual ownership and costs. A failed request never replaces it.
internal sealed class SteamLibrary
{
    internal const string SnapshotKey = "steam.library";
    internal const string RetryKey = "steam.retryUntil";
    private const int ResponseLimit = 8 * 1024 * 1024;
    private static readonly SemaphoreSlim SyncLock = new(1);
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(25) };
    private readonly DbContextOptions<AppDbContext>? options;
    private readonly HttpClient client;
    private readonly ISteamCredentials credentials;
    private readonly TimeProvider clock;

    internal SteamLibrary(DbContextOptions<AppDbContext>? options = null, HttpClient? client = null,
        ISteamCredentials? credentials = null, TimeProvider? clock = null)
    {
        this.options = options;
        this.client = client ?? SharedClient;
        this.credentials = credentials ?? new SteamCredentials();
        this.clock = clock ?? TimeProvider.System;
    }

    private AppDbContext Context() => options == null ? new() : new(options);

    internal static SteamLibrarySnapshot? Read(AppDbContext db)
    {
        var json = CatalogService.GetSetting(db, SnapshotKey);
        if (json.Length == 0 || json.Length > ResponseLimit) return null;
        try
        {
            var snapshot = JsonSerializer.Deserialize<SteamLibrarySnapshot>(json);
            return snapshot is { AppIds: not null } && ValidSteamId(snapshot.SteamId)
                && !snapshot.AppIds.Contains(0) && snapshot.SyncedAtUtc != default ? snapshot : null;
        }
        catch (JsonException) { return null; }
    }

    internal static bool HasSavedLink(AppDbContext db) => CatalogService.GetSetting(db, SnapshotKey).Length > 0;

    internal static bool IsStale(AppDbContext db, SteamLibrarySnapshot snapshot) =>
        snapshot.SyncedAtUtc < DateTime.UtcNow.AddDays(-1)
        || CatalogService.GetSetting(db, "steam.result").Length > 0
        || ActiveCooldown(db, DateTimeOffset.UtcNow) != null;

    internal static string Status(AppDbContext db, SteamLibrarySnapshot? saved)
    {
        var status = saved == null
            ? HasSavedLink(db) ? "Saved Steam evidence unavailable. Relink or unlink in Settings." : "Steam not linked."
            : $"Steam · last synced {saved.SyncedAtUtc.ToLocalTime():g}"
                + (IsStale(db, saved) ? " · saved evidence may be out of date" : "");
        var cooldown = ActiveCooldown(db, DateTimeOffset.UtcNow);
        var result = cooldown?.Message ?? CatalogService.GetSetting(db, "steam.result");
        return status
            + (result.Length > 0 ? $" · {result}" : "");
    }

    private static SteamRateLimitException? ActiveCooldown(AppDbContext db, DateTimeOffset now) =>
        DateTimeOffset.TryParse(CatalogService.GetSetting(db, RetryKey), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var retryAt) && retryAt > now ? new(retryAt) : null;

    private void SaveCooldown(SteamRateLimitException limit)
    {
        using var db = Context();
        CatalogService.SetSetting(db, RetryKey, limit.RetryAt.ToString("O"));
        if (HasSavedLink(db))
            CatalogService.SetSetting(db, "steam.result", "Steam rate limited the last sync. Showing saved library evidence.");
        db.SaveChanges();
    }

    internal async Task LinkAsync(string profile, string key, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        if (key.Length != 32 || !key.All(Uri.IsHexDigit))
            throw new ArgumentException("Enter your 32-character Steam Web API key.");
        // Validate locally before touching credentials or making any requests.
        var (steamId, vanity) = ParseProfile(profile);
        using var work = UpdateRestartGuard.Shared.Enter();
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var cooldown = await Task.Run(() =>
            {
                using var db = Context();
                return ActiveCooldown(db, clock.GetUtcNow());
            }, cancellationToken);
            if (cooldown != null) throw cooldown;
            steamId ??= await ResolveAsync(vanity!, key, cancellationToken);
            var apps = await FetchAsync(steamId, key, cancellationToken);
            await Task.Run(() => Save(new(steamId, DateTime.UtcNow, apps), key, cancellationToken), cancellationToken);
        }
        catch (SteamRateLimitException limit)
        {
            await Task.Run(() => SaveCooldown(limit), CancellationToken.None);
            throw;
        }
        finally { SyncLock.Release(); }
    }

    internal async Task<string> RefreshAsync(bool manual, CancellationToken cancellationToken = default)
    {
        using var work = UpdateRestartGuard.Shared.Enter();
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            var (saved, status) = await Task.Run<(SteamLibrarySnapshot?, string)>(() =>
            {
                using var db = Context();
                var snapshot = Read(db);
                if (snapshot == null)
                    return (null, manual && HasSavedLink(db) ? "Saved Steam evidence unavailable. Relink or unlink in Settings." : "");
                if (!manual && CatalogService.GetSetting(db, "steam.automatic") == "false") return (null, "");
                if (ActiveCooldown(db, clock.GetUtcNow()) is { } pending) return (null, pending.Message);
                if (!manual && DateTime.TryParse(CatalogService.GetSetting(db, "steam.lastAttempt"),
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var attempted)
                    && attempted > DateTime.UtcNow.AddDays(-1)) return (null, "");
                CatalogService.SetSetting(db, "steam.lastAttempt", DateTime.UtcNow.ToString("O"));
                db.SaveChanges();
                return (snapshot, "");
            }, cancellationToken);
            if (saved == null) return status;
            try
            {
                var key = await Task.Run(credentials.Get, cancellationToken);
                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException("Steam key unavailable. Relink your account in Settings.");
                var apps = await FetchAsync(saved.SteamId, key, cancellationToken);
                await Task.Run(() => Save(new(saved.SteamId, DateTime.UtcNow, apps), null, cancellationToken), cancellationToken);
                return "Steam library synced.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (SteamRateLimitException limit)
            {
                await Task.Run(() => SaveCooldown(limit), CancellationToken.None);
                return limit.Message + " Showing saved library evidence.";
            }
            catch (Exception ex)
            {
                // HTTP exceptions can contain the API key in their URL. Never trace or persist them.
                System.Diagnostics.Trace.TraceWarning($"Steam sync failed ({ex.GetType().Name}).");
                const string message = "Steam sync unavailable. Showing saved library evidence. Check your key and Game details privacy in Settings, then retry.";
                await Task.Run(() =>
                {
                    using var db = Context();
                    CatalogService.SetSetting(db, "steam.result", message);
                    db.SaveChanges();
                }, cancellationToken);
                return message;
            }
        }
        finally { SyncLock.Release(); }
    }

    internal async Task UnlinkAsync(CancellationToken cancellationToken = default)
    {
        using var work = UpdateRestartGuard.Shared.Enter();
        await SyncLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                using var db = Context();
                using var transaction = db.Database.BeginTransaction();
                db.CatalogSettings.RemoveRange(db.CatalogSettings.Where(s => s.Key == SnapshotKey
                    || s.Key == "steam.result" || s.Key == "steam.lastAttempt"));
                db.SaveChanges();
                cancellationToken.ThrowIfCancellationRequested();
                var previousKey = credentials.Get();
                credentials.Remove();
                try { transaction.Commit(); }
                catch
                {
                    RestoreCredential(previousKey);
                    throw;
                }
            }, cancellationToken);
        }
        finally { SyncLock.Release(); }
    }

    private void Save(SteamLibrarySnapshot snapshot, string? replacementKey, CancellationToken cancellationToken)
    {
        using var db = Context();
        using var transaction = db.Database.BeginTransaction();
        CatalogService.SetSetting(db, SnapshotKey, JsonSerializer.Serialize(snapshot));
        CatalogService.SetSetting(db, "steam.lastAttempt", snapshot.SyncedAtUtc.ToString("O"));
        CatalogService.SetSetting(db, "steam.result", "");
        if (db.CatalogSettings.Find(RetryKey) is { } cooldown) db.CatalogSettings.Remove(cooldown);
        db.SaveChanges();
        cancellationToken.ThrowIfCancellationRequested();
        var previousKey = replacementKey == null ? null : credentials.Get();
        var keySaved = false;
        try
        {
            if (replacementKey != null) { credentials.Save(replacementKey); keySaved = true; }
            transaction.Commit();
        }
        catch
        {
            if (keySaved) RestoreCredential(previousKey);
            throw;
        }
    }

    private void RestoreCredential(string? previousKey)
    {
        try { if (previousKey == null) credentials.Remove(); else credentials.Save(previousKey); }
        catch { System.Diagnostics.Trace.TraceWarning("Steam credential rollback unavailable. Relink in Settings."); }
    }

    internal static bool ValidSteamId(string value) => ulong.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var id) && id > 76561197960265728UL && id <= 76561202255233023UL;

    internal static (string? SteamId, string? Vanity) ParseProfile(string input)
    {
        input = input.Trim();
        if (ValidSteamId(input)) return (input, null);
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort
            && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length == 2)
            {
                if (parts[0] == "profiles" && ValidSteamId(parts[1])) return (parts[1], null);
                if (parts[0] == "id" && parts[1].Length is > 0 and <= 100
                    && parts[1].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return (null, parts[1]);
            }
        }
        throw new ArgumentException("Enter a SteamID64 or an https://steamcommunity.com/profiles/… or /id/… profile URL.");
    }

    private async Task<string> ResolveAsync(string vanity, string key, CancellationToken cancellationToken)
    {
        using var json = await GetAsync("ISteamUser/ResolveVanityURL/v1/", key,
            "vanityurl=" + Uri.EscapeDataString(vanity), cancellationToken);
        if (json.RootElement.TryGetProperty("response", out var response)
            && response.TryGetProperty("success", out var success) && success.TryGetInt32(out var code) && code == 1
            && response.TryGetProperty("steamid", out var id) && id.ValueKind == JsonValueKind.String
            && ValidSteamId(id.GetString()!)) return id.GetString()!;
        throw new InvalidOperationException("Steam profile not found. Check the profile URL and retry.");
    }

    private async Task<HashSet<uint>> FetchAsync(string steamId, string key, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Serialize(new { steamid = steamId, include_appinfo = false, include_played_free_games = true });
        using var json = await GetAsync("IPlayerService/GetOwnedGames/v1/", key,
            "input_json=" + Uri.EscapeDataString(input), cancellationToken);
        if (!json.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("game_count", out var count) || !count.TryGetInt32(out var total) || total < 0)
            throw new InvalidOperationException("Steam did not return a visible library. Set Game details to Public in Steam privacy settings and retry.");
        var apps = new HashSet<uint>();
        if (response.TryGetProperty("games", out var games))
        {
            if (games.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid Steam library.");
            foreach (var game in games.EnumerateArray())
            {
                if (!game.TryGetProperty("appid", out var id) || !id.TryGetUInt32(out var appId) || appId == 0 || !apps.Add(appId))
                    throw new InvalidDataException("Invalid Steam library.");
            }
        }
        if (apps.Count != total) throw new InvalidDataException("Steam returned an incomplete library. Retry later.");
        return apps;
    }

    private async Task<JsonDocument> GetAsync(string method, string key, string parameters, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        cancellationToken = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.steampowered.com/{method}?key={Uri.EscapeDataString(key)}&{parameters}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var now = clock.GetUtcNow();
            var retry = response.Headers.RetryAfter;
            var retryAt = retry?.Date ?? (retry?.Delta is { } delta && delta > TimeSpan.Zero && delta <= DateTimeOffset.MaxValue - now
                ? now + delta : now.AddMinutes(1));
            throw new SteamRateLimitException(retryAt > now ? retryAt : now.AddMinutes(1));
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Steam rejected the API key. Check your key and retry.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Steam is unavailable. Retry later.");
        if (response.Content.Headers.ContentLength > ResponseLimit) throw new InvalidDataException("Steam response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > ResponseLimit) throw new InvalidDataException("Steam response is too large.");
            bytes.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }
}

internal static class SteamGameMatching
{
    private static readonly Lazy<Dictionary<string, uint>> StoreIds = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EarningsTracker.Data.steam-app-mappings.json")
            ?? throw new InvalidDataException("Steam identity mappings are missing. Repair the application installation.");
        return JsonSerializer.Deserialize<Dictionary<string, uint>>(stream)
            ?? throw new InvalidDataException("Steam identity mappings could not be read.");
    });

    internal static bool IsOwned(GameMetadata? metadata, IEnumerable<StoreOffer> offers, SteamLibrarySnapshot? library,
        CatalogService.SteamIdentityDocument? identities = null) =>
        Match(metadata, offers, library, identities) == SteamMatch.Matched;

    internal static SteamMatch Match(GameMetadata? metadata, IEnumerable<StoreOffer> offers, SteamLibrarySnapshot? library,
        CatalogService.SteamIdentityDocument? identities) =>
        MatchAppIds(metadata, offers, library?.AppIds, identities);

    internal static SteamMatch MatchAppIds(GameMetadata? metadata, IEnumerable<StoreOffer> offers,
        IReadOnlySet<uint>? appIds, CatalogService.SteamIdentityDocument? identities)
    {
        if (appIds == null) return SteamMatch.Unknown;
        if (metadata?.Reviewed != true) return SteamMatch.Unmapped;
        if (identities != null)
        {
            if (!identities.Identities.TryGetValue(metadata.CatalogId, out var identity) || identity.State != "Reviewed"
                || !identity.Apps.Any(app => app.Kind is "FullGame" or "Demo" or "Prologue"))
                return SteamMatch.Unmapped;
            return identity.Apps.Any(app => appIds.Contains(app.AppId)) ? SteamMatch.Matched : SteamMatch.NotObserved;
        }
        var steamOffers = offers.Where(o => CatalogSources.PopularName(o.Source) == "Steam").ToList();
        // An app licence does not prove ownership of deluxe editions, packages or bundles.
        if (steamOffers.Any(o => o.Edition != "Base game")) return SteamMatch.Unmapped;
        var ids = new HashSet<uint>();
        if (metadata.ItadId != null && StoreIds.Value.TryGetValue(metadata.ItadId, out var mappedId) && mappedId > 0)
            ids.Add(mappedId);
        foreach (var offer in steamOffers)
            if (Uri.TryCreate(offer.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && uri.Host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase) && uri.UserInfo.Length == 0 && uri.IsDefaultPort)
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length >= 2 && parts[0] == "app"
                    && uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) ids.Add(id);
            }
        return ids.Count switch
        {
            0 => SteamMatch.Unmapped,
            1 => appIds.Contains(ids.Single()) ? SteamMatch.Matched : SteamMatch.NotObserved,
            _ => SteamMatch.Ambiguous
        };
    }
}

internal enum SteamMatch { Unknown, Matched, Unmapped, NotObserved, Ambiguous }
