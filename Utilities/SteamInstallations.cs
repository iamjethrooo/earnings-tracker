using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EarningsTracker.Utilities;

internal static partial class SteamInstallations
{
    internal sealed record Scan(Dictionary<uint, string> Installed, bool IsIncomplete);

    internal const string SettingsKey = "steam.localLibraryFolders";
    internal const int MaxFolders = 8;
    private const int MaxManifests = 4096;
    private const int MaxManifestBytes = 65536;

    internal static bool IsLibraryFolder(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), "steamapps",
            StringComparison.OrdinalIgnoreCase);

    internal static Scan? Read(string setting)
    {
        if (setting.Length == 0) return null;
        string[]? folders;
        try { folders = JsonSerializer.Deserialize<string[]>(setting); }
        catch (JsonException) { return new([], true); }
        var installed = new Dictionary<uint, string>();
        if (folders is not { Length: > 0 and <= MaxFolders }) return new(installed, true);
        var buffer = new char[MaxManifestBytes + 1];
        var incomplete = false;

        foreach (var folder in folders)
        {
            try
            {
                if (folder == null || !IsLibraryFolder(folder)
                    || new DriveInfo(Path.GetPathRoot(folder)!).DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    incomplete = true;
                    continue;
                }
                var count = 0;
                foreach (var manifest in Directory.EnumerateFiles(folder, "appmanifest_*.acf"))
                {
                    if (++count > MaxManifests)
                    {
                        incomplete = true;
                        break;
                    }
                    try
                    {
                        if (!uint.TryParse(Path.GetFileNameWithoutExtension(manifest).AsSpan("appmanifest_".Length),
                                NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                            continue;
                        if ((File.GetAttributes(manifest) & FileAttributes.ReparsePoint) != 0)
                        {
                            incomplete = true;
                            continue;
                        }
                        using var stream = File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (stream.Length > MaxManifestBytes)
                        {
                            incomplete = true;
                            continue;
                        }
                        using var reader = new StreamReader(stream);
                        var length = reader.ReadBlock(buffer);
                        if (length > MaxManifestBytes)
                        {
                            incomplete = true;
                            continue;
                        }
                        if (ReadInstall(new string(buffer, 0, length), id) is { } game)
                        {
                            try
                            {
                                var attributes = File.GetAttributes(Path.Combine(folder, "common", game.Directory));
                                if ((attributes & FileAttributes.Directory) != 0)
                                    installed.TryAdd(id, game.Name);
                            }
                            catch (FileNotFoundException) { }
                            catch (DirectoryNotFoundException) { }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { incomplete = true; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { incomplete = true; }
        }
        return new(installed, incomplete);
    }

    // Read the AppState fields only. Ignore nested sections and unfamiliar manifest layouts.
    private static (string Directory, string Name)? ReadInstall(string manifest, uint appId)
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
        var name = fields.GetValueOrDefault("name")?.Trim();
        return (directory, string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl) ? directory : name);
    }

    [GeneratedRegex("^\"(appid|stateflags|installdir|name)\"\\s+\"([^\"\\\\]*)\"$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ManifestField();
}
