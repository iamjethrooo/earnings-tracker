using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Utilities;

namespace EarningsTracker.ViewModels;

internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppSettings settings;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string SubscriptionStatus { get; private set; } = "";
    public string UpdateStatus { get; private set; } = "";
    public string RateStatus { get; private set; } = "";
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    public string AssemblyVersionLabel => $"Assembly version {Version}";
    // ReleaseVersion is assigned explicitly when a release is prepared; assembly defaults are not product versions.
    public string BuildLabel => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "ReleaseVersion")?.Value is { Length: > 0 } release
            ? $"Version {release}" : "Development build";
    public string DataFolder => AppDataPaths.DirectoryPath;
    public ApplicationUpdates? ApplicationUpdates { get; }
    public bool CheckApplicationOnStartup => settings.Get("applicationUpdates.startup") != "false";
    internal void SetApplicationStartup(bool value) => settings.Set("applicationUpdates.startup", value ? "true" : "false");
    internal SettingsViewModel(DbContextOptions<AppDbContext>? options = null, ApplicationUpdates? applicationUpdates = null)
    { ApplicationUpdates = applicationUpdates; settings = new(options); Reload(); }
    internal bool GamePass => settings.Subscriptions().Contains("PC Game Pass");
    internal bool AutomaticUpdates => settings.Get("updates.automatic") != "false";
    internal void SetGamePass(bool value) => settings.SaveSubscriptions(value ? ["PC Game Pass"] : []);
    internal void SetAutomaticUpdates(bool value) => settings.Set("updates.automatic", value ? "true" : "false");
    private static string Date(string value) => DateTimeOffset.TryParse(value, out var date)
        ? date.ToLocalTime().ToString("g") : "Never";
    internal void Reload()
    {
        using var db = settings.Context();
        var activeIds = db.Games.Where(g => !g.IsArchived).Select(g => g.Id).ToHashSet();
        var entries = db.SubscriptionAvailability.AsNoTracking().ToList()
            .Where(s => activeIds.Contains(s.GameId) && s.Service == "PC Game Pass" && s.Included).ToList();
        var current = entries.Where(s => CatalogPresentation.Current(s, DateTime.UtcNow)).Select(s => s.GameId).Distinct().Count();
        var stale = entries.Where(s => !CatalogPresentation.Current(s, DateTime.UtcNow)).Select(s => s.GameId).Distinct().Count();
        SubscriptionStatus = $"{current} verified games · {stale} expired or stale entries";
        UpdateStatus = $"Last attempt: {Date(settings.Get("updates.lastAttempt"))}\nCatalog checked: {Date(settings.Get("updates.catalogSuccess"))}\nLive prices checked: {Date(settings.Get("updates.pricesSuccess"))}\nExchange rates checked: {Date(settings.Get("updates.ratesSuccess"))}\nCatalog snapshot: {Date(settings.Get("catalog.published"))}\n{settings.Get("updates.result")}";
        var rate = db.ExchangeRates.AsNoTracking().SingleOrDefault(r => r.Currency == "PHP");
        RateStatus = rate == null ? "No saved PHP exchange rate. Refresh to check rates."
            : $"1 USD = {rate.PerUsd:N4} PHP · rate dated {rate.Date:yyyy-MM-dd}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    internal string Diagnostics()
    {
        // Explicit allowlist: no credentials, paths, identifiers, or financial records.
        var keys = new[] { "updates.lastAttempt", "updates.catalogSuccess", "updates.pricesSuccess", "updates.ratesSuccess", "catalog.published" };
        return JsonSerializer.Serialize(new {
            AppVersion = Version, GeneratedAtUtc = DateTime.UtcNow, Region = "PH", PriceCurrency = "PHP",
            EarningsCurrency = "USD", AutomaticUpdates, Timestamps = keys.ToDictionary(k => k, k => settings.Get(k)),
            LastUpdateResult = SafeResult(settings.Get("updates.result"))
        }, new JsonSerializerOptions { WriteIndented = true });
    }
    private static string SafeResult(string value)
    {
        // Results are app-generated messages; export only recognized outcomes.
        var outcomes = new[] { "Catalog update unavailable; keeping saved data.", "Exchange rates unavailable; keeping saved rates.",
            "Live prices unavailable; keeping saved prices. Check your key or retry later.", "Exchange rates checked." };
        return string.Join(" ", outcomes.Where(value.Contains));
    }
}
