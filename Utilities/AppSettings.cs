using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;

namespace nerve_earnings_tracker.Utilities;

internal sealed class AppSettings(DbContextOptions<AppDbContext>? options = null)
{
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
