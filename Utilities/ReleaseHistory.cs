using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EarningsTracker.Utilities;

internal sealed record ReleaseHistorySection
{
    public string Heading { get; init; } = "";
    public string[] Changes { get; init; } = [];
}

internal sealed record ReleaseHistoryEntry
{
    public string? Version { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool Unreleased { get; init; }
    public string Summary { get; init; } = "";
    public string[] UpgradeNotes { get; init; } = [];
    public ReleaseHistorySection[] Sections { get; init; } = [];
    [JsonIgnore] public bool IsInstalled { get; init; }
    [JsonIgnore] public bool IsExpanded { get; set; }
    [JsonIgnore] public string Heading => Unreleased ? "Unreleased" : $"Version {Version}";
    [JsonIgnore] public string AccessibleName => IsInstalled ? $"{Heading}, Installed" : Heading;
    [JsonIgnore] public string Status => IsInstalled ? "Installed" : "";
    [JsonIgnore] public string DateLabel => Unreleased ? "Not yet published" : PublishedAt!.Value.UtcDateTime.ToString("d MMM yyyy '· UTC'", CultureInfo.InvariantCulture);
    [JsonIgnore] public bool HasUpgradeNotes => UpgradeNotes.Length > 0;
    [JsonIgnore] public bool HasNoDetails => Sections.Length == 0;
}

internal static class ReleaseHistory
{
    internal static string? InstalledVersion => typeof(ReleaseHistory).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "ReleaseVersion")?.Value;

    internal static IReadOnlyList<ReleaseHistoryEntry> Load(string? installedVersion)
    {
        using var stream = typeof(ReleaseHistory).Assembly.GetManifestResourceStream("EarningsTracker.Resources.ReleaseHistory.json")
            ?? throw new InvalidDataException("Bundled release history is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), installedVersion);
    }

    internal static IReadOnlyList<ReleaseHistoryEntry> Parse(string json, string? installedVersion)
    {
        var entries = JsonSerializer.Deserialize<ReleaseHistoryEntry[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Release history is empty.");
        var versions = new HashSet<Version>();
        var unreleasedCount = 0;
        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Summary) || entry.Sections == null || entry.UpgradeNotes == null
                || entry.UpgradeNotes.Any(string.IsNullOrWhiteSpace)
                || entry.Sections.Any(section => section == null || string.IsNullOrWhiteSpace(section.Heading)
                    || section.Changes == null || section.Changes.Length == 0 || section.Changes.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidDataException("Release history contains incomplete notes.");
            if (entry.Unreleased)
            {
                if (++unreleasedCount > 1 || entry.PublishedAt != null || entry.Version != null)
                    throw new InvalidDataException("Unreleased notes must be separate from published versions.");
            }
            else if (!Version.TryParse(entry.Version, out var version) || version.Build < 0 || version.Revision >= 0
                || entry.PublishedAt == null || !versions.Add(version))
                throw new InvalidDataException("Published releases require unique three-part versions and publication dates.");
        }

        return entries.OrderByDescending(entry => entry.Unreleased)
            .ThenByDescending(entry => entry.Unreleased ? null : new Version(entry.Version!))
            .Select(entry => entry with
            {
                IsInstalled = !entry.Unreleased && entry.Version == installedVersion,
                IsExpanded = !entry.Unreleased && entry.Version == installedVersion
            }).ToArray();
    }
}
