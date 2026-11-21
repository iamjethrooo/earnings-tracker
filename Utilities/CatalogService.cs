using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities;

public sealed class CatalogSnapshot
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Version { get; set; } = "";
    [JsonRequired] public DateTime PublishedAt { get; set; }
    [JsonRequired] public List<CatalogEntry> Games { get; set; } = [];
    internal string? PayloadHash { get; set; }
    internal string? Payload { get; set; }
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
    public bool? SupportsMultiplayer { get; set; }
    public string MultiplayerProvenance { get; set; } = "";
    public DateTime? MultiplayerVerifiedAt { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<StoreOffer> Offers { get; set; } = [];
    public List<SubscriptionAvailability> Subscriptions { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CatalogSteamIdentity? SteamIdentity { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogRosterAddition? RosterAddition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CatalogRateVariant? RateVariant { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogRateVariant(
    [property: JsonRequired] string GameName,
    [property: JsonRequired] decimal RatePerHour,
    [property: JsonRequired] int MinutesCap,
    [property: JsonRequired] string LegacyCatalogId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogRosterAddition(
    [property: JsonRequired] decimal RatePerHour,
    [property: JsonRequired] int MinutesCap,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GameName = null);

public sealed record CatalogSteamIdentity(string State, string? ReviewedAt, string Provenance, List<CatalogSteamApp> Apps);
public sealed record CatalogSteamApp(uint AppId, string Kind);

internal static class CatalogService
{
    private const string SchemaKey = "catalog.schema";
    private const string SteamIdentitiesKey = "catalog.steam-identities";
    private const string ActivePayloadKey = "catalog.active-payload";
    private const string PreviousPayloadKey = "catalog.previous-payload";
    private static readonly Regex UtcTimestamp = new(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z");

    internal sealed record SteamIdentityDocument(int SchemaVersion, string Version, string Sha256,
        Dictionary<string, CatalogSteamIdentity> Identities);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    internal static CatalogSnapshot Parse(string json, string? payloadHash = null)
    {
        var payload = json;
        json = json.TrimStart('\uFEFF');
        var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty catalog.");
        using var document = JsonDocument.Parse(json);
        var published = document.RootElement.EnumerateObject().Last(p =>
            p.Name.Equals(nameof(CatalogSnapshot.PublishedAt), StringComparison.OrdinalIgnoreCase)).Value.GetString();
        if (published == null || !UtcTimestamp.IsMatch(published) || snapshot.PublishedAt == default)
            throw new InvalidDataException("Catalog publication time must be a UTC timestamp.");
        if (snapshot.Games == null || snapshot.Games.Any(entry => entry == null || entry.Offers == null
            || entry.Subscriptions == null || entry.Genres == null || entry.Aliases == null
            || entry.Offers.Any(offer => offer == null) || entry.Subscriptions.Any(sub => sub == null)))
            throw new InvalidDataException("Invalid catalog entries.");
        snapshot.PublishedAt = snapshot.PublishedAt.ToUniversalTime();
        foreach (var entry in snapshot.Games)
        {
            entry.VerifiedAt = entry.VerifiedAt?.ToUniversalTime();
            entry.MultiplayerVerifiedAt = entry.MultiplayerVerifiedAt?.ToUniversalTime();
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
        snapshot.PayloadHash = payloadHash ?? Hash(Encoding.UTF8.GetBytes(payload));
        snapshot.Payload = payload;
        return snapshot;
    }

    internal static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    internal static void Validate(CatalogSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(snapshot.Version) || snapshot.PublishedAt == default
            || snapshot.PublishedAt > DateTime.UtcNow || snapshot.Games.Count > 10000)
            throw new InvalidDataException("Unsupported or invalid catalog header.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rosterRates = new Dictionary<string, HashSet<decimal>>(StringComparer.OrdinalIgnoreCase);
        var rateVariants = new HashSet<(string Name, decimal Rate, int Cap)>();
        var titleCounts = snapshot.Games.SelectMany(entry => entry.Aliases.Prepend(entry.Name))
            .GroupBy(name => name?.Trim() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Games)
        {
            if (string.IsNullOrWhiteSpace(entry.CatalogId) || !ids.Add(entry.CatalogId)
                || string.IsNullOrWhiteSpace(entry.Name) || !names.Add(entry.Name + "#" + entry.NameOccurrence))
                throw new InvalidDataException("Duplicate or missing catalog identity.");
            if (entry.Summary == null || entry.CoverUrl == null || entry.CoverSha256 == null || entry.Provenance == null)
                throw new InvalidDataException($"Invalid catalog metadata for {entry.CatalogId}.");
            if (snapshot.SchemaVersion == 2 && (entry.SteamIdentity == null
                || !ValidSteamIdentity(entry.SteamIdentity, snapshot.PublishedAt)
                || (entry.SteamIdentity.State == "Reviewed" && !entry.Reviewed)))
                throw new InvalidDataException($"Invalid Steam identity for {entry.CatalogId}.");
            if (entry.NameOccurrence.HasValue && (entry.ExpectedNameCount is not > 1
                || entry.NameOccurrence < 1 || entry.NameOccurrence > entry.ExpectedNameCount || entry.Aliases.Count > 0))
                throw new InvalidDataException("Invalid duplicate title occurrence mapping.");
            foreach (var alias in entry.Aliases)
                if (string.IsNullOrWhiteSpace(alias) || !names.Add(alias + "#"))
                    throw new InvalidDataException("Duplicate catalog alias.");
            if (entry.RateVariant is { } variant)
            {
                if (!entry.Reviewed || entry.RosterAddition != null || entry.NameOccurrence.HasValue
                    || entry.ExpectedNameCount.HasValue || entry.Aliases.Count != 0
                    || !ValidRosterTitle(entry.Name) || !ValidRosterTitle(variant.GameName)
                    || string.Equals(entry.Name, variant.GameName, StringComparison.OrdinalIgnoreCase)
                    || variant.RatePerHour <= 0 || variant.RatePerHour > int.MaxValue
                    || decimal.Round(variant.RatePerHour, 2) != variant.RatePerHour || variant.MinutesCap <= 0
                    || !rateVariants.Add((variant.GameName.ToUpperInvariant(), variant.RatePerHour, variant.MinutesCap))
                    || !snapshot.Games.Any(legacy => legacy.CatalogId == variant.LegacyCatalogId
                        && legacy.CatalogId != entry.CatalogId && legacy.RateVariant == null && legacy.Reviewed
                        && (string.Equals(legacy.Name, variant.GameName, StringComparison.OrdinalIgnoreCase)
                            || legacy.Aliases.Contains(variant.GameName, StringComparer.OrdinalIgnoreCase))))
                    throw new InvalidDataException($"Invalid metadata rate variant for {entry.CatalogId}.");
            }
            if (entry.RosterAddition is { } addition)
            {
                var gameName = addition.GameName ?? entry.Name;
                if (snapshot.SchemaVersion != 2 || !entry.Reviewed
                    || entry.NameOccurrence.HasValue || entry.ExpectedNameCount.HasValue || !ValidRosterTitle(entry.Name)
                    || addition.RatePerHour <= 0 || addition.RatePerHour > int.MaxValue
                    || decimal.Round(addition.RatePerHour, 2) != addition.RatePerHour || addition.MinutesCap <= 0
                    || entry.Aliases.Prepend(entry.Name).Any(name => titleCounts[name.Trim()] != 1)
                    || (addition.GameName != null && (!ValidRosterTitle(gameName) || entry.Aliases.Count != 0
                        || string.Equals(gameName, entry.Name, StringComparison.OrdinalIgnoreCase))))
                    throw new InvalidDataException($"Invalid roster addition for {entry.CatalogId}.");
                if (!rosterRates.TryGetValue(gameName, out var rates))
                    rosterRates.Add(gameName, rates = []);
                if (!rates.Add(addition.RatePerHour))
                    throw new InvalidDataException($"Duplicate roster rate for {gameName}.");
            }
            if (entry.CoverUrl != "" && (!IsHttps(entry.CoverUrl) || entry.CoverSha256?.Length != 64
                || !entry.CoverSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Artwork requires HTTPS and a SHA-256 checksum.");
            if (entry.Genres.Any(string.IsNullOrWhiteSpace) || entry.Genres.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Genres.Count)
                throw new InvalidDataException("Invalid genres.");
            if (entry.SupportsMultiplayer.HasValue)
            {
                if (!entry.Reviewed || string.IsNullOrWhiteSpace(entry.MultiplayerProvenance) || entry.MultiplayerProvenance.Length > 4096
                    || entry.MultiplayerVerifiedAt is not { } checkedAt || checkedAt == default || checkedAt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                    throw new InvalidDataException("Multiplayer metadata requires a reviewed identity and dated source evidence.");
            }
            else if (entry.MultiplayerProvenance != "" || entry.MultiplayerVerifiedAt.HasValue)
                throw new InvalidDataException("Multiplayer metadata requires a reviewed identity and dated source evidence.");
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

    internal static bool ValidCurrency(string value) => value?.Length == 3 && value.All(c => c >= 'A' && c <= 'Z');

    private static bool ValidRosterTitle(string name) => !string.IsNullOrWhiteSpace(name)
        && name == name.Trim() && name.Length <= 256 && !name.Any(char.IsControl);

    private static bool ValidSteamIdentity(CatalogSteamIdentity identity, DateTime publishedAt)
    {
        if (string.IsNullOrWhiteSpace(identity.Provenance) || identity.Provenance.Length > 4096 || identity.Apps == null)
            return false;
        if (identity.State == "Unresolved") return identity.ReviewedAt == null && identity.Apps.Count == 0;
        if (identity.ReviewedAt is not { } timestamp
            || !UtcTimestamp.IsMatch(timestamp)
            || !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var reviewedAt)
            || reviewedAt == default || reviewedAt.UtcDateTime > publishedAt.ToUniversalTime()) return false;
        if (identity.State is "NotApplicable" or "Withdrawn") return identity.Apps.Count == 0;
        if (identity.State != "Reviewed" || identity.Apps.Count is < 1 or > 32) return false;

        uint previous = 0;
        string? kind = null;
        foreach (var app in identity.Apps)
        {
            if (app == null || app.AppId <= previous || app.Kind is not ("FullGame" or "Demo" or "Prologue" or "Dlc" or "Tool")
                || (kind != null && app.Kind != kind)) return false;
            previous = app.AppId;
            kind = app.Kind;
        }
        return true;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static SteamIdentityDocument? ReadSteamIdentities(AppDbContext db)
    {
        if (GetSetting(db, SchemaKey) != "2") return null;
        var json = GetSetting(db, SteamIdentitiesKey);
        try
        {
            var document = json.Length == 0 ? null : JsonSerializer.Deserialize<SteamIdentityDocument>(json, JsonOptions);
            if (document is { SchemaVersion: 2, Sha256.Length: 64, Identities: not null }
                && document.Version == GetSetting(db, "catalog.version") && document.Sha256.All(Uri.IsHexDigit)
                && DateTime.TryParse(GetSetting(db, "catalog.published"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var publishedAt)
                && document.Identities.All(pair => !string.IsNullOrWhiteSpace(pair.Key)
                    && pair.Value != null && ValidSteamIdentity(pair.Value, publishedAt))) return document;
        }
        catch (JsonException) { }
        throw new InvalidDataException("The active catalog's Steam identity mappings are unavailable.");
    }

    internal enum ActivationStatus { Applied, UpToDate, Older, Incompatible, Rejected }
    internal sealed record ActivationResult(ActivationStatus Status, List<string> Unmatched,
        RosterPreview? Roster = null, int GamesAdded = 0);
    internal sealed record RosterCandidate(string CatalogId, string Name, CatalogRosterAddition Terms);
    internal sealed record RosterPreview(List<RosterCandidate> Additions, List<string> Conflicts);
    private sealed record SavedPayload(int SchemaVersion, string Version, string Sha256, string Payload, HashSet<string>? MatchedIds = null);
    internal sealed record RollbackVersions(string Active, string Target);

    internal static List<string> Import(AppDbContext db, CatalogSnapshot snapshot) => Activate(db, snapshot).Unmatched;

    internal static ActivationResult Activate(AppDbContext db, CatalogSnapshot snapshot,
        CancellationToken cancellationToken = default, bool checkedRemotely = false,
        bool previewRoster = false, RosterPreview? approvedRoster = null)
        => Activate(db, snapshot, cancellationToken, checkedRemotely, null, previewRoster, approvedRoster);

    internal static RollbackVersions GetRollbackVersions(AppDbContext db)
    {
        var active = GetSetting(db, "catalog.version");
        var (previous, _) = ReadPreviousSnapshot(db);
        if (active.Length == 0 || active == previous.Version)
            throw new InvalidDataException("There is no previous catalog version to restore.");
        return new(active, previous.Version);
    }

    internal static void Rollback(AppDbContext db, RollbackVersions confirmed, CancellationToken cancellationToken = default)
        => Activate(db, null, cancellationToken, false, confirmed, false, null);

    private static (CatalogSnapshot Snapshot, HashSet<string>? MatchedIds) ReadPreviousSnapshot(AppDbContext db)
    {
        var raw = GetSetting(db, PreviousPayloadKey);
        if (raw.Length == 0)
            throw new InvalidDataException("No validated previous catalog is available. Recovery becomes available after a compatible catalog update replaces a saved snapshot.");
        SavedPayload? saved;
        try { saved = JsonSerializer.Deserialize<SavedPayload>(raw); }
        catch (JsonException ex) { throw new InvalidDataException("The previous catalog record is unreadable.", ex); }
        if (saved?.Payload == null || Hash(Encoding.UTF8.GetBytes(saved.Payload)) != saved.Sha256)
            throw new InvalidDataException("The previous catalog failed its integrity check.");
        var snapshot = Parse(saved.Payload, saved.Sha256);
        if (snapshot.Version != saved.Version || snapshot.SchemaVersion != saved.SchemaVersion)
            throw new InvalidDataException("The previous catalog identity does not match its saved record.");
        return (snapshot, saved.MatchedIds);
    }

    private static ActivationResult Activate(AppDbContext db, CatalogSnapshot? incoming,
        CancellationToken cancellationToken, bool checkedRemotely, RollbackVersions? rollback,
        bool previewRoster, RosterPreview? approvedRoster)
    {
        using var transaction = db.Database.BeginTransaction();
        var (snapshot, restoreMembers) = incoming == null ? ReadPreviousSnapshot(db) : (incoming, null);
        var versionKey = "catalog.version";
        var activeVersion = GetSetting(db, versionKey);
        if (rollback != null && (activeVersion != rollback.Active || snapshot.Version != rollback.Target
            || activeVersion == snapshot.Version))
            throw new InvalidDataException("The catalog changed after confirmation. Review the versions and try again.");
        Validate(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var payload = snapshot.Payload ?? JsonSerializer.Serialize(snapshot, JsonOptions);
        var hash = snapshot.PayloadHash ?? Hash(Encoding.UTF8.GetBytes(payload));
        var activeSchema = GetSetting(db, SchemaKey);
        if (rollback == null && snapshot.SchemaVersion == 1 && activeSchema == "2")
            return new(ActivationStatus.Incompatible, []);
        var knownHash = GetSetting(db, "catalog.hash." + snapshot.Version);
        if (knownHash.Length > 0 && knownHash != hash)
            throw new InvalidDataException("A catalog version cannot be reused for a different snapshot.");
        var activePublished = GetSetting(db, "catalog.published");
        if (rollback == null && GetSetting(db, "catalog.rejected." + snapshot.Version) == hash)
            return new(ActivationStatus.Rejected, []);
        var alreadyActive = activeVersion == snapshot.Version;
        if (alreadyActive && snapshot.SchemaVersion == 2 && knownHash.Length == 0
            && (ReadSteamIdentities(db) is not { } active || active.Sha256 != hash))
            throw new InvalidDataException("A catalog version cannot be reused for a different snapshot.");
        if (!alreadyActive && rollback == null
            && DateTime.TryParse(activePublished, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            && (snapshot.PublishedAt < last || (snapshot.PublishedAt == last
                && ((snapshot.SchemaVersion == 2 && activeSchema != "2")
                    || string.CompareOrdinal(snapshot.Version, activeVersion) <= 0))))
            return new(ActivationStatus.Older, []);

        var games = db.Games.ToList();
        var metadata = db.GameMetadata.ToList();
        var variantIds = snapshot.Games.Where(entry => entry.RosterAddition?.GameName != null || entry.RateVariant != null)
            .Select(entry => entry.CatalogId).ToHashSet(StringComparer.Ordinal);
        var variantNames = snapshot.Games.Where(entry => entry.RosterAddition?.GameName != null)
            .Select(entry => entry.RosterAddition!.GameName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedVariantGameIds = metadata.Where(link => variantIds.Contains(link.CatalogId))
            .Select(link => link.GameId).ToHashSet();
        var legacyGames = games.Where(game => !linkedVariantGameIds.Contains(game.Id)).ToList();
        var roster = PlanRoster(snapshot, games, legacyGames, metadata, variantNames,
            out var conflicts, out var existingRosterGames);
        if (previewRoster)
            return new(alreadyActive ? ActivationStatus.UpToDate : ActivationStatus.Applied, [], roster);
        if (approvedRoster != null && (!approvedRoster.Additions.SequenceEqual(roster.Additions)
            || !approvedRoster.Conflicts.SequenceEqual(roster.Conflicts)))
            throw new InvalidDataException("The game list changed after preview. Check for updates again.");

        var additions = approvedRoster?.Additions ?? [];
        // Automatic checks and older clients can activate the same payload before roster approval.
        // A startup roster repair can add a local rate after this exact snapshot was activated.
        // Materialize its reviewed metadata without changing the immutable snapshot or any game.
        var pendingRateMapping = snapshot.Games.Any(entry => entry.RateVariant is { } variant
            && !metadata.Any(link => link.CatalogId == entry.CatalogId)
            && FindRateVariantMatches(variant, games) is { Count: 1 } candidates
            && !metadata.Any(link => link.GameId == candidates[0].Id));
        if (alreadyActive && additions.Count == 0 && !pendingRateMapping)
        {
            if (checkedRemotely)
            {
                SetSetting(db, "updates.catalogSuccess", DateTime.UtcNow.ToString("O"));
                db.SaveChanges();
                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
            }
            return new(ActivationStatus.UpToDate, [], roster);
        }
        // Import mutates offer and subscription entity keys. Keep the validated input reusable.
        snapshot = Parse(payload, hash);
        var addedGames = GameService.AddFromCatalog(db, additions, snapshot.Version);
        if (rollback != null)
        {
            var rejectedHash = GetSetting(db, "catalog.hash." + activeVersion);
            if (rejectedHash.Length == 0)
                throw new InvalidDataException("The active catalog identity is unavailable. Refresh the catalog before trying recovery again.");
            SetSetting(db, "catalog.rejected." + activeVersion, rejectedHash);
            db.CatalogSettings.Remove(db.CatalogSettings.Find(PreviousPayloadKey)!);
        }
        else if (!alreadyActive && GetSetting(db, ActivePayloadKey) is { Length: > 0 } activePayload)
        {
            SetSetting(db, PreviousPayloadKey, activePayload);
            db.CatalogSettings.RemoveRange(db.CatalogSettings.Where(row => row.Key == "catalog.previous-materialized"));
        }
        var unresolved = new List<string>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var members = restoreMembers ?? snapshot.Games.Select(entry => entry.CatalogId).ToHashSet(StringComparer.Ordinal);
        foreach (var omitted in metadata.Where(link => !members.Contains(link.CatalogId)))
        {
            omitted.IgdbId = null; omitted.ItadId = null;
            omitted.Summary = ""; omitted.CoverUrl = ""; omitted.CoverSha256 = "";
            omitted.Provenance = "Unresolved"; omitted.Reviewed = false; omitted.VerifiedAt = null;
            omitted.SupportsMultiplayer = null; omitted.MultiplayerProvenance = ""; omitted.MultiplayerVerifiedAt = null;
            db.GameGenres.RemoveRange(db.GameGenres.Where(x => x.GameId == omitted.GameId));
            db.StoreOffers.RemoveRange(db.StoreOffers.Where(x => x.GameId == omitted.GameId));
            db.SubscriptionAvailability.RemoveRange(db.SubscriptionAvailability.Where(x => x.GameId == omitted.GameId));
            RemoveLivePriceMarker(db, omitted.GameId);
        }
        // Legacy links are assigned first even if a producer places rate variants first.
        foreach (var entry in snapshot.Games.OrderBy(entry => entry.RateVariant != null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (conflicts.Contains(entry.CatalogId) || (restoreMembers != null && !restoreMembers.Contains(entry.CatalogId)))
            {
                unresolved.Add(entry.Name);
                continue;
            }
            var existing = metadata.SingleOrDefault(x => x.CatalogId == entry.CatalogId);
            var matches = addedGames.TryGetValue(entry.CatalogId, out var added) ? [added]
                : existing != null ? games.Where(x => x.Id == existing.GameId).ToList()
                : existingRosterGames.TryGetValue(entry.CatalogId, out var rosterGame) ? [rosterGame]
                : entry.RateVariant is { } variant ? FindRateVariantMatches(variant, games)
                : entry.RosterAddition == null ? FindTitleMatches(entry, legacyGames) : [];
            if (existing == null && entry.NameOccurrence.HasValue)
                matches = matches.Count == entry.ExpectedNameCount
                    ? [matches[entry.NameOccurrence.Value - 1]] : [];
            if (matches.Count != 1) { unresolved.Add(entry.Name); continue; }
            var game = matches[0];
            existing ??= metadata.SingleOrDefault(x => x.GameId == game.Id);
            if (entry.RateVariant is { } rateVariant && existing != null && existing.CatalogId != entry.CatalogId)
            {
                // The original identity may already own either rate, depending on upgrade history.
                // Keep it; this variant is only needed for the independently unlinked rate.
                if (existing.CatalogId != rateVariant.LegacyCatalogId)
                    unresolved.Add(entry.Name);
                continue;
            }
            if (existing != null && existing.CatalogId != entry.CatalogId)
                throw new InvalidDataException($"Conflicting mapping for {entry.Name}.");
            matched.Add(entry.CatalogId);
            if (existing == null)
            {
                existing = new GameMetadata { GameId = game.Id, CatalogId = entry.CatalogId };
                db.GameMetadata.Add(existing);
                metadata.Add(existing);
            }
            var samePriceMapping = existing.ItadId == entry.ItadId;
            existing.IgdbId = entry.IgdbId; existing.ItadId = entry.ItadId;
            existing.Summary = entry.Summary; existing.CoverUrl = entry.CoverUrl;
            existing.CoverSha256 = entry.CoverSha256; existing.Provenance = entry.Provenance;
            existing.Reviewed = entry.Reviewed; existing.VerifiedAt = entry.VerifiedAt;
            existing.SupportsMultiplayer = entry.SupportsMultiplayer;
            existing.MultiplayerProvenance = entry.MultiplayerProvenance;
            existing.MultiplayerVerifiedAt = entry.MultiplayerVerifiedAt;
            db.GameGenres.RemoveRange(db.GameGenres.Where(x => x.GameId == game.Id));
            var offersToAdd = MergeOffers(db, game.Id, entry.Offers, samePriceMapping, snapshot.PublishedAt, rollback != null);
            db.SubscriptionAvailability.RemoveRange(db.SubscriptionAvailability.Where(x => x.GameId == game.Id));
            // Flush removals before adding identical composite genre keys.
            db.SaveChanges();
            db.GameGenres.AddRange(entry.Genres.Select(x => new GameGenre { GameId = game.Id, Name = x }));
            foreach (var offer in offersToAdd)
            { offer.Id = 0; offer.GameId = game.Id; db.StoreOffers.Add(offer); }
            foreach (var sub in entry.Subscriptions) { sub.Id = 0; sub.GameId = game.Id; db.SubscriptionAvailability.Add(sub); }
        }
        SetSetting(db, versionKey, snapshot.Version);
        SetSetting(db, "catalog.published", snapshot.PublishedAt.ToString("O"));
        SetSetting(db, ActivePayloadKey, JsonSerializer.Serialize(new SavedPayload(snapshot.SchemaVersion, snapshot.Version, hash, payload, matched)));
        SetSetting(db, "catalog.hash." + snapshot.Version, hash);
        SetSetting(db, SchemaKey, snapshot.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        if (snapshot.SchemaVersion == 2)
        {
            var identities = snapshot.Games.ToDictionary(entry => entry.CatalogId, entry => entry.SteamIdentity!, StringComparer.Ordinal);
            SetSetting(db, SteamIdentitiesKey, JsonSerializer.Serialize(new SteamIdentityDocument(2, snapshot.Version, hash, identities), JsonOptions));
        }
        else if (db.CatalogSettings.Find(SteamIdentitiesKey) is { } identitiesSetting)
        {
            db.CatalogSettings.Remove(identitiesSetting);
        }
        if (checkedRemotely) SetSetting(db, "updates.catalogSuccess", DateTime.UtcNow.ToString("O"));
        db.SaveChanges();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(ActivationStatus.Applied, unresolved, roster, addedGames.Count);
    }

    private static List<Game> FindRateVariantMatches(CatalogRateVariant variant, List<Game> games) => games
        .Where(game => string.Equals(game.Name, variant.GameName, StringComparison.OrdinalIgnoreCase)
            && game.RatePerHour == variant.RatePerHour && game.MinutesCap == variant.MinutesCap).ToList();

    private static List<Game> FindTitleMatches(CatalogEntry entry, List<Game> games)
    {
        var matches = games.Where(game => string.Equals(game.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
            || entry.Aliases.Contains(game.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(game => game.Id).ToList();

        // The published bundled identity was seeded at $2 in v1.6.0. When no stable link exists,
        // keep that mapping unambiguous after adding back $1.50. Existing stable links win above.
        // Other duplicate/customized terms continue through the normal ambiguity safeguards.
        if (entry.CatalogId == "6cfcc31f-10b6-430d-a828-9a611b75172e"
            && !entry.NameOccurrence.HasValue && matches.Count == 2
            && matches.All(game => string.Equals(game.Name, "Predecessor", StringComparison.OrdinalIgnoreCase)
                && !game.IsArchived && game.MinutesCap == 3000m)
            && matches.Select(game => game.RatePerHour).Order().SequenceEqual(new[] { 1.5m, 2m }))
            return matches.Where(game => game.RatePerHour == 2m).ToList();

        return matches;
    }

    private static List<StoreOffer> MergeOffers(AppDbContext db, int gameId, List<StoreOffer> incoming,
        bool samePriceMapping, DateTime publishedAt, bool restoring)
    {
        var saved = db.StoreOffers.Where(offer => offer.GameId == gameId).ToList();
        var incomingLiveSource = incoming.Where(IsItad).ToList();
        // A metadata-only publication advances the snapshot date without refreshing its quotes.
        var preserveLiveBatch = samePriceMapping
            && DateTime.TryParse(GetSetting(db, "prices.live." + gameId), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var liveAt)
            && (liveAt > publishedAt || (incomingLiveSource.Count > 0
                && incomingLiveSource.All(offer => offer.VerifiedAt <= liveAt)));
        if (!preserveLiveBatch) RemoveLivePriceMarker(db, gameId);

        var keep = new HashSet<StoreOffer>();
        if (preserveLiveBatch) keep.UnionWith(saved.Where(IsItad));
        var additions = new List<StoreOffer>();
        var comparable = saved.Where(offer => !restoring && (samePriceMapping || !IsItad(offer)))
            .ToLookup(OfferKey);
        foreach (var group in incoming.Where(offer => !preserveLiveBatch || !IsItad(offer)).GroupBy(OfferKey))
        {
            var previous = comparable[group.Key].ToList();
            if (previous.Count == 1 && group.Count() == 1 && previous[0].VerifiedAt > group.Single().VerifiedAt)
                keep.Add(previous[0]);
            else
                additions.AddRange(group);
        }
        db.StoreOffers.RemoveRange(saved.Where(offer => !keep.Contains(offer)));
        return additions;
    }

    private static bool IsItad(StoreOffer offer) => offer.Provenance == "IsThereAnyDeal";

    private static (string Source, string Edition, string Region, string Currency, string Provenance, string Url)
        OfferKey(StoreOffer offer) => (offer.Source, offer.Edition, offer.Region, offer.Currency, offer.Provenance,
            IsItad(offer) ? "" : offer.Url);

    private static RosterPreview PlanRoster(CatalogSnapshot snapshot, List<Game> games, List<Game> legacyGames,
        List<GameMetadata> metadata, HashSet<string> variantNames,
        out HashSet<string> conflicts, out Dictionary<string, Game> existingRosterGames)
    {
        conflicts = new(StringComparer.Ordinal);
        existingRosterGames = new(StringComparer.Ordinal);
        var preview = new RosterPreview([], []);
        var linkedCatalogIds = metadata.Select(link => link.CatalogId).ToHashSet(StringComparer.Ordinal);
        var linkedGameIds = metadata.Select(link => link.GameId).ToHashSet();
        foreach (var entry in snapshot.Games.OrderBy(entry => entry.CatalogId, StringComparer.Ordinal))
        {
            if (entry.RosterAddition is not { } terms || linkedCatalogIds.Contains(entry.CatalogId))
                continue;
            if (terms.GameName is { } gameName)
            {
                // An existing same-rate row requires an explicit stable link before it can be reused.
                if (games.Any(game => string.Equals(game.Name.Trim(), gameName, StringComparison.OrdinalIgnoreCase)
                    && game.RatePerHour == terms.RatePerHour))
                {
                    conflicts.Add(entry.CatalogId);
                    preview.Conflicts.Add(entry.Name);
                }
                else
                    preview.Additions.Add(new(entry.CatalogId, gameName, terms));
                continue;
            }
            var titles = entry.Aliases.Prepend(entry.Name).Select(title => title.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sharedTitle = variantNames.Contains(entry.Name);
            var candidates = (sharedTitle ? games.Where(game => game.RatePerHour == terms.RatePerHour) : legacyGames)
                .Where(game => titles.Contains(game.Name.Trim())).ToList();
            if (candidates.Count == 0)
                preview.Additions.Add(new(entry.CatalogId, entry.Name, terms));
            else if (candidates.Count != 1 || linkedGameIds.Contains(candidates[0].Id)
                || (sharedTitle && candidates[0].MinutesCap != terms.MinutesCap)
                || FindTitleMatches(entry, candidates).Count != 1)
            {
                conflicts.Add(entry.CatalogId);
                preview.Conflicts.Add(entry.Name);
            }
            else
                existingRosterGames.Add(entry.CatalogId, candidates[0]);
        }
        return preview;
    }

    internal static string GetSetting(AppDbContext db, string key) => db.CatalogSettings.Find(key)?.Value ?? "";
    private static void RemoveLivePriceMarker(AppDbContext db, int gameId)
    {
        if (db.CatalogSettings.Find("prices.live." + gameId) is { } marker)
            db.CatalogSettings.Remove(marker);
    }

    internal static void SetSetting(AppDbContext db, string key, string value)
    {
        var setting = db.CatalogSettings.Find(key);
        if (setting == null) db.CatalogSettings.Add(new CatalogSetting { Key = key, Value = value });
        else setting.Value = value;
    }

    internal static void ImportBundled(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json");
        if (!File.Exists(path)) return;
        var bytes = File.ReadAllBytes(path);
        Import(db, Parse(new UTF8Encoding(false, true).GetString(bytes), Hash(bytes)));
    }
}
