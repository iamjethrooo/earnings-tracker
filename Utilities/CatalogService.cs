using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities;

public sealed class CatalogSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Version { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public List<CatalogEntry> Games { get; set; } = [];
}

public sealed class CatalogEntry
{
    public string CatalogId { get; set; } = "";
    public string Name { get; set; } = "";
    // Reviewed duplicate seed records remain distinct, in their original ID order.
    public int? NameOccurrence { get; set; }
    public int? ExpectedNameCount { get; set; }
    public List<string> Aliases { get; set; } = [];
    public long? IgdbId { get; set; }
    public string? ItadId { get; set; }
    public string Summary { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string CoverSha256 { get; set; } = "";
    public string Provenance { get; set; } = "Unresolved";
    public bool Reviewed { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<StoreOffer> Offers { get; set; } = [];
    public List<SubscriptionAvailability> Subscriptions { get; set; } = [];
}

internal static class CatalogService
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    internal static CatalogSnapshot Parse(string json)
    {
        var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty catalog.");
        snapshot.PublishedAt = snapshot.PublishedAt.ToUniversalTime();
        foreach (var entry in snapshot.Games)
        {
            entry.VerifiedAt = entry.VerifiedAt?.ToUniversalTime();
            foreach (var offer in entry.Offers)
            {
                offer.VerifiedAt = offer.VerifiedAt.ToUniversalTime();
                offer.ExpiresAt = offer.ExpiresAt?.ToUniversalTime();
            }
            foreach (var subscription in entry.Subscriptions)
            {
                subscription.VerifiedAt = subscription.VerifiedAt.ToUniversalTime();
                subscription.ExpiresAt = subscription.ExpiresAt?.ToUniversalTime();
            }
        }
        Validate(snapshot);
        return snapshot;
    }

    internal static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    internal static void Validate(CatalogSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1 || string.IsNullOrWhiteSpace(snapshot.Version) || snapshot.PublishedAt == default
            || snapshot.PublishedAt > DateTime.UtcNow.AddMinutes(5) || snapshot.Games.Count > 10000)
            throw new InvalidDataException("Unsupported or invalid catalog header.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Games)
        {
            if (string.IsNullOrWhiteSpace(entry.CatalogId) || !ids.Add(entry.CatalogId)
                || string.IsNullOrWhiteSpace(entry.Name) || !names.Add(entry.Name + "#" + entry.NameOccurrence))
                throw new InvalidDataException("Duplicate or missing catalog identity.");
            if (entry.NameOccurrence.HasValue && (entry.ExpectedNameCount is not > 1
                || entry.NameOccurrence < 1 || entry.NameOccurrence > entry.ExpectedNameCount || entry.Aliases.Count > 0))
                throw new InvalidDataException("Invalid duplicate title occurrence mapping.");
            foreach (var alias in entry.Aliases)
                if (string.IsNullOrWhiteSpace(alias) || !names.Add(alias + "#"))
                    throw new InvalidDataException("Duplicate catalog alias.");
            if (entry.CoverUrl != "" && (!IsHttps(entry.CoverUrl) || entry.CoverSha256.Length != 64
                || !entry.CoverSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Artwork requires HTTPS and a SHA-256 checksum.");
            if (entry.Genres.Any(string.IsNullOrWhiteSpace) || entry.Genres.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Genres.Count)
                throw new InvalidDataException("Invalid genres.");
            if (!entry.Reviewed && (entry.CoverUrl != "" || entry.Genres.Count > 0 || entry.Offers.Count > 0 || entry.Subscriptions.Count > 0))
                throw new InvalidDataException("Unreviewed matches cannot publish enrichment.");
            foreach (var offer in entry.Offers)
            {
                if (!IsHttps(offer.Url) || string.IsNullOrWhiteSpace(offer.Source) || offer.RegularPrice < 0 || offer.SalePrice < 0
                    || !Enum.IsDefined(offer.Kind) || !ValidCurrency(offer.Currency) || offer.Region != "PH"
                    || offer.VerifiedAt == default || offer.VerifiedAt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5)
                    || string.IsNullOrWhiteSpace(offer.Provenance))
                    throw new InvalidDataException($"Invalid store offer for {entry.Name} ({offer.Source}).");
            }
            foreach (var sub in entry.Subscriptions)
                if (!IsHttps(sub.Url) || string.IsNullOrWhiteSpace(sub.Service) || sub.Platform != "PC" || sub.Region != "PH"
                    || sub.VerifiedAt == default || sub.VerifiedAt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5) || string.IsNullOrWhiteSpace(sub.Provenance))
                    throw new InvalidDataException("Invalid PC subscription availability.");
        }
    }

    internal static bool ValidCurrency(string value) => value.Length == 3 && value.All(c => c >= 'A' && c <= 'Z');

    // A snapshot never creates/deletes earning games or touches user-owned data.
    internal static List<string> Import(AppDbContext db, CatalogSnapshot snapshot)
    {
        Validate(snapshot);
        var versionKey = "catalog.version";
        if (GetSetting(db, versionKey) == snapshot.Version) return [];
        if (DateTime.TryParse(GetSetting(db, "catalog.published"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            && snapshot.PublishedAt < last) return [];
        var games = db.Games.ToList();
        var metadata = db.GameMetadata.ToList();
        var unresolved = new List<string>();
        using var transaction = db.Database.BeginTransaction();
        foreach (var entry in snapshot.Games)
        {
            var existing = metadata.SingleOrDefault(x => x.CatalogId == entry.CatalogId);
            var matches = existing == null
                ? games.Where(x => string.Equals(x.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                    || entry.Aliases.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).OrderBy(x => x.Id).ToList()
                : games.Where(x => x.Id == existing.GameId).ToList();
            if (existing == null && entry.NameOccurrence.HasValue)
                matches = matches.Count == entry.ExpectedNameCount
                    ? [matches[entry.NameOccurrence.Value - 1]] : [];
            if (matches.Count != 1) { unresolved.Add(entry.Name); continue; }
            var game = matches[0];
            existing ??= metadata.SingleOrDefault(x => x.GameId == game.Id);
            if (existing != null && existing.CatalogId != entry.CatalogId)
                throw new InvalidDataException($"Conflicting mapping for {entry.Name}.");
            if (existing == null)
            {
                existing = new GameMetadata { GameId = game.Id, CatalogId = entry.CatalogId };
                db.GameMetadata.Add(existing);
                metadata.Add(existing);
            }
            existing.IgdbId = entry.IgdbId; existing.ItadId = entry.ItadId;
            existing.Summary = entry.Summary; existing.CoverUrl = entry.CoverUrl;
            existing.CoverSha256 = entry.CoverSha256; existing.Provenance = entry.Provenance;
            existing.Reviewed = entry.Reviewed; existing.VerifiedAt = entry.VerifiedAt;
            var preserveLivePrices = DateTime.TryParse(GetSetting(db, "prices.live." + game.Id), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var liveAt) && liveAt > snapshot.PublishedAt;
            db.GameGenres.RemoveRange(db.GameGenres.Where(x => x.GameId == game.Id));
            db.StoreOffers.RemoveRange(db.StoreOffers.Where(x => x.GameId == game.Id && (!preserveLivePrices || x.Provenance != "IsThereAnyDeal")));
            db.SubscriptionAvailability.RemoveRange(db.SubscriptionAvailability.Where(x => x.GameId == game.Id));
            // Flush removals before adding identical composite genre keys.
            db.SaveChanges();
            db.GameGenres.AddRange(entry.Genres.Select(x => new GameGenre { GameId = game.Id, Name = x }));
            foreach (var offer in entry.Offers.Where(x => !preserveLivePrices || x.Provenance != "IsThereAnyDeal"))
            { offer.Id = 0; offer.GameId = game.Id; db.StoreOffers.Add(offer); }
            foreach (var sub in entry.Subscriptions) { sub.Id = 0; sub.GameId = game.Id; db.SubscriptionAvailability.Add(sub); }
        }
        SetSetting(db, versionKey, snapshot.Version);
        SetSetting(db, "catalog.published", snapshot.PublishedAt.ToString("O"));
        db.SaveChanges(); transaction.Commit();
        return unresolved;
    }

    internal static string GetSetting(AppDbContext db, string key) => db.CatalogSettings.Find(key)?.Value ?? "";
    internal static void SetSetting(AppDbContext db, string key, string value)
    {
        var setting = db.CatalogSettings.Find(key);
        if (setting == null) db.CatalogSettings.Add(new CatalogSetting { Key = key, Value = value });
        else setting.Value = value;
    }

    internal static void ImportBundled(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json");
        if (File.Exists(path)) Import(db, Parse(File.ReadAllText(path)));
    }
}
