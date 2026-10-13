using System.IO;

namespace nerve_earnings_tracker.Utilities;

internal static class AppDataPaths
{
    // Opt-in process isolation for real launch tests; normal user storage is unchanged.
    internal const string OverrideVariable = "NERVE_TRACKER_DATA_DIRECTORY";
    internal static bool HasOverride => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OverrideVariable));
    internal static string DirectoryPath
    {
        get
        {
            var isolated = Environment.GetEnvironmentVariable(OverrideVariable);
            if (string.IsNullOrEmpty(isolated))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NerveEarningsTracker");
            if (!Path.IsPathFullyQualified(isolated))
                throw new InvalidOperationException($"{OverrideVariable} must be an absolute directory path.");
            return Path.GetFullPath(isolated);
        }
    }
}
