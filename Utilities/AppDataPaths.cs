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
    internal static string DefaultDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarningsTracker");
    internal static string DevelopmentDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EarningsTracker.Development");
    internal static string LegacyDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NerveEarningsTracker");
    internal static bool UsesProductionStorage => !IsDebugBuild && !HasOverride && !ApplicationUpdateConfiguration.RequiresIsolatedStorage;
    // Keep the former/current production lock; development and scratch profiles have their own locks.
    internal static string InstanceDirectoryPath => UsesProductionStorage ? LegacyDirectoryPath : DirectoryPath;
    internal static string DirectoryPath => ResolveDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), IsDebugBuild,
        Environment.GetEnvironmentVariable(OverrideVariable), ApplicationUpdateConfiguration.RequiresIsolatedStorage);

    internal static string ResolveDirectory(string localAppData, bool debug, string? isolated, bool requireIsolation)
    {
        var production = Path.Combine(localAppData, "EarningsTracker");
        var legacy = Path.Combine(localAppData, "NerveEarningsTracker");
        var development = Path.Combine(localAppData, "EarningsTracker.Development");
        if (requireIsolation)
            ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest", isolated, production, legacy, development);
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
            debug ? new[] { production, legacy } : new[] { development });
        RejectLinkedPath(path);
        return path;
    }

    internal static void RejectLinkedPath(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Isolated storage cannot follow linked directories.");
        var database = Path.Combine(path, DatabaseFileName);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(database + suffix) && (File.GetAttributes(database + suffix) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Isolated storage cannot follow linked database files.");
    }

    internal static void EnsureDataLocation()
    {
        // Resolve and validate before any filesystem operation. Fresh development/scratch profiles
        // initialize from migrations and bundles, never by importing a personal legacy database.
        _ = DirectoryPath;
        if (UsesProductionStorage) DataLocationMigration.Migrate(LegacyDirectoryPath, DefaultDirectoryPath);
    }
}
