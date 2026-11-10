using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EarningsTracker.Utilities;

internal static partial class SteamInstallations
{
    internal const string SettingsKey = "steam.localLibraryFolders";
    internal const int MaxFolders = 8;
    private const int MaxManifests = 4096;
    private const int MaxManifestBytes = 65536;

    internal static bool IsLibraryFolder(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), "steamapps",
            StringComparison.OrdinalIgnoreCase);

    internal static HashSet<uint>? Read(string setting)
    {
        if (setting.Length == 0) return null;
        string[]? folders;
        try { folders = JsonSerializer.Deserialize<string[]>(setting); }
        catch (JsonException) { return []; }
        var installed = new HashSet<uint>();
        if (folders == null || folders.Length > MaxFolders) return installed;
        var buffer = new char[MaxManifestBytes + 1];

        foreach (var folder in folders.Where(path => path != null && IsLibraryFolder(path)))
        {
            try
            {
                if (new DriveInfo(Path.GetPathRoot(folder)!).DriveType is not (DriveType.Fixed or DriveType.Removable))
                    continue;
                foreach (var manifest in Directory.EnumerateFiles(folder, "appmanifest_*.acf").Take(MaxManifests))
                {
                    try
                    {
                        if (!uint.TryParse(Path.GetFileNameWithoutExtension(manifest).AsSpan("appmanifest_".Length),
                                NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                            || (File.GetAttributes(manifest) & FileAttributes.ReparsePoint) != 0)
                            continue;
                        using var stream = File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (stream.Length > MaxManifestBytes) continue;
                        using var reader = new StreamReader(stream);
                        var length = reader.ReadBlock(buffer);
                        if (length <= MaxManifestBytes && ReadInstallDirectory(new string(buffer, 0, length), id) is { } directory
                            && Directory.Exists(Path.Combine(folder, "common", directory)))
                            installed.Add(id);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return installed;
    }

    // Read the AppState fields only. Ignore nested sections and unfamiliar manifest layouts.
    private static string? ReadInstallDirectory(string manifest, uint appId)
    {
        using var reader = new StringReader(manifest);
        if (reader.ReadLine()?.Trim() != "\"AppState\"" || reader.ReadLine()?.Trim() != "{") return null;
        var depth = 1;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed == "{") { depth++; continue; }
            if (trimmed == "}") { if (--depth == 0) break; continue; }
            if (depth != 1) continue;
            var field = ManifestField().Match(trimmed);
            if (field.Success && !fields.TryAdd(field.Groups[1].Value, field.Groups[2].Value)) return null;
        }
        if (depth != 0 || appId == 0
            || fields.GetValueOrDefault("appid") != appId.ToString(CultureInfo.InvariantCulture)
            || fields.GetValueOrDefault("StateFlags") != "4"
            || !fields.TryGetValue("installdir", out var directory)
            || directory.Length == 0 || directory is "." or ".."
            || directory.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        return directory;
    }

    [GeneratedRegex("^\"(appid|stateflags|installdir)\"\\s+\"([^\"\\\\]*)\"$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManifestField();
}
