using Microsoft.Win32;
using System.IO;
using System.Security;
using System.Text.Json;

namespace EarningsTracker.Utilities;

internal sealed record LocalInstallation(string Name, string Source, bool HasLocalFolder, uint? SteamAppId = null)
{
    public string Evidence => SteamAppId.HasValue ? $"Steam · App ID {SteamAppId}"
        : HasLocalFolder ? Source : $"{Source} · registration only";
    public string AccessibleName => $"{Name}, {Evidence}";
}

internal sealed record LocalInstallationSnapshot(bool IsConfigured, IReadOnlyList<LocalInstallation> Entries,
    HashSet<uint>? SteamAppIds)
{
    internal IReadOnlyList<string> IncompleteSources { get; init; } = [];
}

internal static class LocalGameInstallations
{
    internal const string WindowsKey = "installations.windows";
    internal const string FoldersKey = "installations.folders";
    internal const int MaxFolders = 32;
    internal const string SteamSource = "Steam";
    internal const string WindowsSource = "Windows installed apps";
    internal const string FolderSource = "Selected folder";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private const int MaxRegistryEntriesPerView = 4096;

    internal sealed record WindowsRegistration(string? Name, string? Location, bool IsHidden);
    internal sealed record WindowsScan(IReadOnlyList<WindowsRegistration> Registrations, bool IsIncomplete);

    internal static LocalInstallationSnapshot Read(string steamSetting, string folderSetting, bool includeWindows,
        Func<WindowsScan>? readWindows = null)
    {
        var steam = SteamInstallations.Read(steamSetting);
        var entries = new List<LocalInstallation>();
        var incompleteSources = new List<string>();
        if (steam != null)
        {
            entries.AddRange(steam.Installed.Select(game => new LocalInstallation(game.Value, SteamSource, true, game.Key)));
            if (steam.IsIncomplete) incompleteSources.Add(SteamSource);
        }
        var folders = ReadFolders(folderSetting);
        var incompleteFolders = folderSetting.Length != 0 && folders.Length == 0;
        foreach (var path in folders)
        {
            if (HasLocalFolder(path))
                entries.Add(new(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), FolderSource, true));
            else
                incompleteFolders = true;
        }
        if (incompleteFolders) incompleteSources.Add(FolderSource);
        if (includeWindows)
        {
            var windows = (readWindows ?? ReadWindows)();
            if (windows.IsIncomplete) incompleteSources.Add(WindowsSource);
            foreach (var registration in windows.Registrations)
            {
                var name = registration.Name?.Trim();
                if (registration.IsHidden || string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl))
                    continue;
                entries.Add(new(name, WindowsSource, HasLocalFolder(registration.Location)));
            }
        }
        var ordered = entries.GroupBy(entry => (entry.Name.ToUpperInvariant(), entry.Source, entry.SteamAppId))
            .Select(group => group.FirstOrDefault(entry => entry.HasLocalFolder) ?? group.First())
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ToList();
        return new(steam != null || folderSetting.Length != 0 || includeWindows, ordered, steam?.Installed.Keys.ToHashSet())
        {
            IncompleteSources = incompleteSources
        };
    }

    internal static string[] ReadFolders(string setting)
    {
        if (setting.Length == 0) return [];
        try
        {
            var paths = JsonSerializer.Deserialize<string[]>(setting);
            return paths is { Length: <= MaxFolders }
                ? paths.Where(IsLocalFolderPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch (JsonException) { return []; }
    }

    internal static bool IsLocalFolderPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !string.IsNullOrEmpty(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));

    private static bool HasLocalFolder(string? path)
    {
        if (!IsLocalFolderPath(path)) return false;
        try
        {
            var directory = new DirectoryInfo(path!);
            if (new DriveInfo(directory.Root.FullName).DriveType is not (DriveType.Fixed or DriveType.Removable))
                return false;
            // Avoid following redirected install folders or touching an unselected network share.
            var ancestors = new Stack<DirectoryInfo>();
            for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
                ancestors.Push(ancestor);
            foreach (var ancestor in ancestors)
            {
                if (!ancestor.Exists || (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static WindowsScan ReadWindows()
    {
        var entries = new List<WindowsRegistration>();
        var incomplete = false;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall == null) continue;
                var keys = uninstall.GetSubKeyNames();
                if (keys.Length > MaxRegistryEntriesPerView) incomplete = true;
                foreach (var key in keys.Take(MaxRegistryEntriesPerView))
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(key);
                        if (entry == null)
                        {
                            incomplete = true;
                            continue;
                        }
                        entries.Add(new(entry.GetValue("DisplayName") as string, entry.GetValue("InstallLocation") as string,
                            entry.GetValue("SystemComponent") is 1 || entry.GetValue("ParentKeyName") is string { Length: > 0 }));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    { incomplete = true; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            { incomplete = true; }
        }
        return new(entries, incomplete);
    }
}
