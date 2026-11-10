using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities;

internal sealed class CatalogManifest
{
    public int SchemaVersion { get; set; }
    public string? Version { get; set; }
    public string? MinimumClientVersion { get; set; }
    public string CatalogUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class CatalogUpdates
{
    private const string ReleaseRoot = "https://github.com/iamjethrooo/earnings-tracker/releases/download/";
    internal const string ManifestUrl = ReleaseRoot + "catalog-latest/manifest.json";
    internal const string SchemaTwoManifestUrl = ReleaseRoot + "catalog-v2-latest/manifest.json";
    private static readonly Regex VersionTag = new(@"\Acatalog-[A-Za-z0-9][A-Za-z0-9.-]{0,119}\z");
    private static readonly Regex ClientVersionFormat = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z");
    private readonly HttpClient _client;
    private readonly HttpClient _catalogClient;
    private readonly Version? _clientVersion;
    private readonly DbContextOptions<AppDbContext>? _options;
    private static readonly SemaphoreSlim RefreshLock = new(1);
    internal CatalogUpdates(DbContextOptions<AppDbContext>? options = null, HttpClient? client = null, Version? clientVersion = null)
    {
        _options = options;
        _client = client ?? SharedClient;
        _catalogClient = client ?? CatalogClient;
        _clientVersion = clientVersion ?? (Version.TryParse(ReleaseHistory.InstalledVersion, out var version) ? version : null);
    }
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient CatalogClient = new(new HttpClientHandler { AllowAutoRedirect = false });
    private AppDbContext Context() => _options == null ? new() : new(_options);

    internal async Task<string> RefreshAsync(bool manual, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var updateWork = UpdateRestartGuard.Shared.Enter();
        if (!await RefreshLock.WaitAsync(0)) return "A catalog update is already running.";
        try
        {
            using (var db = Context())
            {
                if (!manual && CatalogService.GetSetting(db, "updates.automatic") == "false") return "";
                if (!manual && DateTime.TryParse(CatalogService.GetSetting(db, "updates.lastAttempt"), out var last)
                    && last.ToUniversalTime() > DateTime.UtcNow.AddDays(-1)) return "";
                CatalogService.SetSetting(db, "updates.lastAttempt", DateTime.UtcNow.ToString("O"));
                db.SaveChanges();
            }
            var messages = new List<string>();
            try
            {
                messages.Add(await RefreshCatalogAsync(cancellationToken));
            }
            catch (Exception ex) when (Recoverable(ex) && !cancellationToken.IsCancellationRequested)
            {
                messages.Add(ex is InvalidDataException or JsonException or DecoderFallbackException
                    ? "Catalog update rejected; keeping saved data."
                    : "Catalog update unavailable; keeping saved data.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            try { await RefreshRatesAsync(); messages.Add("Exchange rates checked.");
                new AppSettings(_options).Set("updates.ratesSuccess", DateTime.UtcNow.ToString("O")); }
            catch (Exception ex) when (Recoverable(ex))
            { messages.Add("Exchange rates unavailable; keeping saved rates."); }
            if (manual)
            {
                try
                {
                    var key = CatalogCredentials.Get();
                    if (!string.IsNullOrWhiteSpace(key)) messages.Add(await RefreshPricesAsync(key));
                }
                catch (Exception ex) when (Recoverable(ex) || ex is System.ComponentModel.Win32Exception)
                { messages.Add("Live prices unavailable; keeping saved prices. Check your key or retry later."); }
            }
            var result = string.Join(" ", messages);
            new AppSettings(_options).Set("updates.result", result);
            return result;
        }
        finally { RefreshLock.Release(); }
    }

    private async Task<string> RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        byte[] manifestBytes;
        var expectedSchema = 2;
        try { manifestBytes = await GetCatalogBytesAsync(SchemaTwoManifestUrl, 65536, token); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            using var db = Context();
            if (CatalogService.GetSetting(db, "catalog.schema") == "2")
                return "Schema 2 catalog feed unavailable; keeping saved data.";
            expectedSchema = 1;
            manifestBytes = await GetCatalogBytesAsync(ManifestUrl, 65536, token);
        }
        var manifest = JsonSerializer.Deserialize<CatalogManifest>(manifestBytes, CatalogService.JsonOptions)
            ?? throw new InvalidDataException("Empty update manifest.");
        if (manifest.SchemaVersion < 1 || (manifest.SchemaVersion != expectedSchema && manifest.SchemaVersion <= 2))
            throw new InvalidDataException("Catalog schema does not match its channel.");
        if (manifest.SchemaVersion > 2)
            return "Catalog update incompatible with this app; keeping saved data.";
        if (manifest.CatalogUrl == null || !manifest.CatalogUrl.StartsWith(ReleaseRoot, StringComparison.Ordinal)
            || manifest.Sha256?.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid catalog manifest.");
        var asset = manifest.CatalogUrl[ReleaseRoot.Length..].Split('/');
        if (asset is not [var versionTag, "catalog.json"] || !VersionTag.IsMatch(versionTag)
            || versionTag is "catalog-latest" or "catalog-v2-latest"
            || ((manifest.Version != null || expectedSchema == 2) && manifest.Version != versionTag))
            throw new InvalidDataException("Catalog manifest must reference its immutable version asset.");
        if (manifest.MinimumClientVersion != null || expectedSchema == 2)
        {
            if (manifest.MinimumClientVersion is not { } minimum || !ClientVersionFormat.IsMatch(minimum)
                || !Version.TryParse(minimum, out var minimumVersion))
                throw new InvalidDataException("Invalid minimum catalog client version.");
            if (_clientVersion == null || _clientVersion < minimumVersion)
                return $"Catalog {versionTag} is available and requires app version {minimum} or later.";
        }
        var bytes = await GetCatalogBytesAsync(manifest.CatalogUrl, 16 * 1024 * 1024, token);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Catalog checksum mismatch.");
        var result = await Task.Run(() =>
        {
            var snapshot = CatalogService.Parse(new UTF8Encoding(false, true).GetString(bytes), hash);
            if (snapshot.SchemaVersion != expectedSchema || snapshot.Version != versionTag)
                throw new InvalidDataException("Catalog payload does not match the manifest.");
            using var db = Context();
            return CatalogService.Activate(db, snapshot, token, checkedRemotely: true);
        }, token);
        return result.Status switch
        {
            CatalogService.ActivationStatus.Applied => $"Catalog {versionTag} applied · {result.Unmatched.Count} unmatched entries.",
            CatalogService.ActivationStatus.UpToDate => $"Catalog {versionTag} is up to date.",
            CatalogService.ActivationStatus.Older => $"Catalog {versionTag} is older than or the same age as saved data.",
            CatalogService.ActivationStatus.Rejected => $"Catalog {versionTag} was rolled back. Keeping the saved catalog until a compatible newer version is available.",
            _ => "Catalog update incompatible with saved data."
        };
    }

    private async Task<byte[]> GetCatalogBytesAsync(string url, int limit, CancellationToken token)
    {
        var uri = new Uri(url);
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("EarningsTracker/1.0");
            using var response = await _catalogClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.IsSuccessStatusCode) return await ReadBoundedAsync(response, limit, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is not { IsAbsoluteUri: true } || !CatalogService.IsHttps(location.AbsoluteUri)
                    || !location.IsDefaultPort || location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com")
                    || location.Fragment.Length != 0)
                    throw new InvalidDataException("Untrusted catalog redirect.");
                uri = location;
                continue;
            }
            throw new HttpRequestException("Catalog request failed.", null, response.StatusCode);
        }
        throw new InvalidDataException("Too many catalog redirects.");
    }

    private static bool Recoverable(Exception ex) => ex is HttpRequestException or OperationCanceledException
        or IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or DecoderFallbackException
        or Microsoft.Data.Sqlite.SqliteException or DbUpdateException;

    private async Task<byte[]> GetBytesAsync(string url, int limit)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request);
        return await ReadBoundedAsync(response, limit);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd("EarningsTracker/1.0");
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            // No immediate retry on 429; the user can retry later or the next daily check will run.
            throw new HttpRequestException(status == HttpStatusCode.TooManyRequests ? "Provider rate limit reached." : "Provider request failed.", null, status);
        }
        return response;
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token = default)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Provider response too large.");
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Provider response too large.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.ToArray();
    }

    internal async Task RefreshRatesAsync()
    {
        using var json = JsonDocument.Parse(await GetBytesAsync("https://api.frankfurter.dev/v2/rates?base=USD", 1024 * 1024));
        var rows = new List<ExchangeRate>();
        foreach (var element in json.RootElement.EnumerateArray())
        {
            var currency = element.GetProperty("quote").GetString()!;
            var rate = element.GetProperty("rate").GetDecimal();
            var date = element.GetProperty("date").GetDateTime();
            if (element.GetProperty("base").GetString() != "USD" || !CatalogService.ValidCurrency(currency)
                || rate <= 0 || date > DateTime.UtcNow.Date || rows.Any(x => x.Currency == currency))
                throw new InvalidDataException("Invalid exchange rate.");
            rows.Add(new ExchangeRate { Currency = currency, PerUsd = rate, Date = date });
        }
        if (!rows.Any(x => x.Currency == "PHP")) throw new InvalidDataException("PHP exchange rate missing.");
        using var db = Context();
        using var transaction = db.Database.BeginTransaction();
        foreach (var rate in rows)
        {
            var existing = db.ExchangeRates.Find(rate.Currency);
            if (existing == null) db.ExchangeRates.Add(rate);
            else if (existing.Date <= rate.Date) { existing.PerUsd = rate.PerUsd; existing.Date = rate.Date; }
        }
        db.SaveChanges(); transaction.Commit();
    }

    internal static List<StoreOffer> ParsePrices(JsonElement row, int gameId, DateTime now)
    {
        var offers = new List<StoreOffer>();
        foreach (var deal in row.GetProperty("deals").EnumerateArray())
        {
            if (!deal.GetProperty("platforms").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "Windows")) continue;
            var amount = deal.GetProperty("price").GetProperty("amount").GetDecimal();
            var regular = deal.GetProperty("regular").GetProperty("amount").GetDecimal();
            var currency = deal.GetProperty("price").GetProperty("currency").GetString()!;
            if (currency != deal.GetProperty("regular").GetProperty("currency").GetString()) throw new InvalidDataException("Offer currencies differ.");
            var expiry = deal.GetProperty("expiry");
            offers.Add(new StoreOffer
            {
                GameId = gameId, Source = deal.GetProperty("shop").GetProperty("name").GetString()!,
                Url = deal.GetProperty("url").GetString()!, Currency = currency,
                RegularPrice = regular, SalePrice = amount, Kind = AcquisitionKind.Purchase,
                VerifiedAt = deal.GetProperty("timestamp").GetDateTimeOffset().UtcDateTime,
                ExpiresAt = expiry.ValueKind == JsonValueKind.Null ? null : expiry.GetDateTimeOffset().UtcDateTime,
                Provenance = "IsThereAnyDeal"
            });
        }
        // A zero-priced deal is not proof of permanent free-to-play or a timed giveaway.
        CatalogService.Validate(new CatalogSnapshot { Version = "live-validation", PublishedAt = now,
            Games = [new CatalogEntry { CatalogId = "live", Name = "Live offer", Reviewed = true, Offers = offers }] });
        return offers;
    }

    internal async Task<string> RefreshPricesAsync(string key)
    {
        List<GameMetadata> mappings;
        using (var db = Context()) mappings = db.GameMetadata.AsNoTracking().Where(x => x.Reviewed && x.ItadId != null).ToList();
        if (mappings.Count == 0) return "No reviewed price-provider mappings yet.";
        var fetched = new Dictionary<int, List<StoreOffer>>();
        foreach (var batch in mappings.Chunk(200))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.isthereanydeal.com/games/prices/v3?country=PH&vouchers=false");
            request.Headers.Add("ITAD-API-Key", key);
            request.Content = JsonContent.Create(batch.Select(x => x.ItadId).ToArray());
            using var response = await SendAsync(request);
            using var json = JsonDocument.Parse(await ReadBoundedAsync(response, 8 * 1024 * 1024));
            foreach (var row in json.RootElement.EnumerateArray())
            {
                var matches = batch.Where(x => x.ItadId == row.GetProperty("id").GetString()).ToList();
                foreach (var match in matches) fetched[match.GameId] = ParsePrices(row, match.GameId, DateTime.UtcNow);
            }
        }
        using (var db = Context())
        {
            using var transaction = db.Database.BeginTransaction();
            foreach (var pair in fetched)
            {
                db.StoreOffers.RemoveRange(db.StoreOffers.Where(x => x.GameId == pair.Key && x.Provenance == "IsThereAnyDeal"));
                db.StoreOffers.AddRange(pair.Value);
                CatalogService.SetSetting(db, "prices.live." + pair.Key, DateTime.UtcNow.ToString("O"));
            }
            db.SaveChanges(); transaction.Commit();
        }
        new AppSettings(_options).Set("updates.pricesSuccess", DateTime.UtcNow.ToString("O"));
        return $"Live prices updated for {fetched.Count} games.";
    }
}
