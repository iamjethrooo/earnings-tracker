using System.IO;

namespace EarningsTracker.Utilities;

internal static class AppDataPaths
{
    internal const string OverrideVariable = "NERVE_TRACKER_DATA_DIRECTORY";
    internal const string DatabaseFileName = "EarningsTracker.db";
    internal const string LegacyDatabaseFileName = "nerve_earnings_tracker.db";
#if DEBUG
    internal const bool IsDebugBuild = true;
#else
    internal const bool IsDebugBuild = false;
#endif
    internal static bool HasOverride => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OverrideVariable));
    internal static string DefaultDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarningsTracker.UserData");
    internal static string FormerDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarningsTracker");
    internal static string DevelopmentDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarningsTracker.Development");
    internal static string LegacyDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NerveEarningsTracker");
    internal static bool UsesProductionStorage => !IsDebugBuild && !HasOverride && !ApplicationUpdateConfiguration.RequiresIsolatedStorage;
    // Keep the former/current production lock; development and scratch profiles have their own locks.
    internal static string InstanceDirectoryPath => ResolveInstanceDirectory(DirectoryPath, DefaultDirectoryPath, LegacyDirectoryPath);
    internal static string DirectoryPath => ResolveDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), IsDebugBuild,
        Environment.GetEnvironmentVariable(OverrideVariable), ApplicationUpdateConfiguration.RequiresIsolatedStorage);

    internal static string ResolveInstanceDirectory(string directory, string production, string legacy)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        // An explicit Release override of production must still share the former app's lock.
        return string.Equals(normalized, Path.TrimEndingDirectorySeparator(Path.GetFullPath(production)), StringComparison.OrdinalIgnoreCase)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacy)) : normalized;
    }

    internal static string ResolveDirectory(string localAppData, bool debug, string? isolated, bool requireIsolation)
    {
        var production = Path.Combine(localAppData, "EarningsTracker.UserData");
        var former = Path.Combine(localAppData, "EarningsTracker");
        var install = Path.Combine(localAppData, "EarningsTracker.App");
        var legacy = Path.Combine(localAppData, "NerveEarningsTracker");
        var development = Path.Combine(localAppData, "EarningsTracker.Development");
        if (requireIsolation)
            ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest", isolated, production, former, legacy, development, install);
        if (string.IsNullOrEmpty(isolated))
        {
            if (debug) RejectLinkedPath(development);
            return debug ? development : production;
        }
        if (!Path.IsPathFullyQualified(isolated))
            throw new InvalidOperationException($"{OverrideVariable} must be an absolute directory path.");
        var path = Path.GetFullPath(isolated);
        // Debug may never target personal data; Release may never target development data.
        ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest", path,
            debug ? new[] { production, former, legacy, install } : new[] { former, legacy, development, install });
        RejectLinkedPath(path);
        return path;
    }

    internal static void RejectLinkedPath(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Isolated storage cannot follow linked directories.");
        var database = Path.Combine(path, DatabaseFileName);
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(database + suffix) && (File.GetAttributes(database + suffix) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Isolated storage cannot follow linked database files.");
    }

    internal static bool EnsureDataLocation()
    {
        // Resolve and validate before any filesystem operation. Fresh development/scratch profiles
        // initialize from migrations and bundles, never by importing a personal legacy database.
        _ = DirectoryPath;
        if (UsesProductionStorage)
            return UserDataMigration.Migrate(DefaultDirectoryPath, FormerDirectoryPath, LegacyDirectoryPath,
                AppContext.BaseDirectory);
        return false;
    }
}
