using System.Globalization;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;

namespace EarningsTracker.Utilities;

internal readonly record struct PersonalGameRatings(int Grinding, int Replay)
{
    internal bool IsValid => Grinding is >= 0 and <= 3 && Replay is >= 0 and <= 3;
}

internal sealed class AppSettings(DbContextOptions<AppDbContext>? options = null)
{
    private const string RatingsPrefix = "games.ratings.";

    internal static Dictionary<int, PersonalGameRatings> ReadGameRatings(AppDbContext db)
    {
        var result = new Dictionary<int, PersonalGameRatings>();
        foreach (var row in db.CatalogSettings.AsNoTracking().Where(row => row.Key.StartsWith(RatingsPrefix)))
        {
            if (!int.TryParse(row.Key.AsSpan(RatingsPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || row.Value.Split(',') is not [var grinding, var replay]
                || !int.TryParse(grinding, NumberStyles.None, CultureInfo.InvariantCulture, out var grindingRating)
                || !int.TryParse(replay, NumberStyles.None, CultureInfo.InvariantCulture, out var replayRating))
                continue;

            var ratings = new PersonalGameRatings(grindingRating, replayRating);
            if (ratings.IsValid) result[id] = ratings;
        }
        return result;
    }

    internal void SaveGameRatings(int gameId, PersonalGameRatings ratings)
    {
        if (!ratings.IsValid) throw new ArgumentOutOfRangeException(nameof(ratings));
        using var db = Context();
        var key = RatingsPrefix + gameId.ToString(CultureInfo.InvariantCulture);
        if (ratings != default)
            CatalogService.SetSetting(db, key, FormattableString.Invariant($"{ratings.Grinding},{ratings.Replay}"));
        else if (db.CatalogSettings.Find(key) is { } existing)
            db.CatalogSettings.Remove(existing);
        db.SaveChanges();
    }

    internal int GamesDefaultSort
    {
        get => int.TryParse(Get("games.defaultSort"), out var value) && value is >= 0 and <= 3 ? value : 2;
        set { if (value is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(value)); Set("games.defaultSort", value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    }
    internal bool GamesBookmarksOnly { get => Get("games.bookmarksOnly") == "true"; set => Set("games.bookmarksOnly", value ? "true" : "false"); }
    internal bool GamesShowArchived { get => Get("games.showArchived") != "false"; set => Set("games.showArchived", value ? "true" : "false"); }
    internal static void InitializeDefaults(AppDbContext db)
    {
        if (db.CatalogSettings.Find("updates.automatic") == null)
            CatalogService.SetSetting(db, "updates.automatic", "true");
        foreach (var service in CatalogSubscriptions.Services)
        {
            var key = "subscription." + service;
            if (db.CatalogSettings.Find(key) == null)
                CatalogService.SetSetting(db, key, "false");
        }
        db.SaveChanges();
    }

    internal AppDbContext Context() => options == null ? new() : new(options);
    internal string Get(string key) { using var db = Context(); return CatalogService.GetSetting(db, key); }
    internal void Set(string key, string value)
    {
        using var db = Context(); CatalogService.SetSetting(db, key, value); db.SaveChanges();
    }
    internal HashSet<string> Subscriptions()
    {
        using var db = Context();
        return CatalogSubscriptions.Services.Where(s => CatalogService.GetSetting(db, "subscription." + s) == "true").ToHashSet();
    }
    internal void SaveSubscriptions(IEnumerable<string> services)
    {
        var selected = services.Intersect(CatalogSubscriptions.Services).ToHashSet();
        using var db = Context();
        foreach (var service in CatalogSubscriptions.Services)
            CatalogService.SetSetting(db, "subscription." + service, selected.Contains(service) ? "true" : "false");
        db.SaveChanges();
    }
}
