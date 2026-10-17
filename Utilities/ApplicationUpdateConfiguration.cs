using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Velopack.Sources;

namespace EarningsTracker.Utilities;

// Build metadata chooses one source. Runtime overrides apply only to isolated local tests.
internal static class ApplicationUpdateConfiguration
{
    internal const string ProductionRepository = "iamjethrooo/earnings-tracker";
    // GitHub redirects the former production name after a rename; it is never a test source.
    internal const string FormerProductionRepository = "iamjethrooo/nerve-earnings-tracker";
    internal const string Channel = "win-x64-app-stable";

    internal static bool RequiresIsolatedStorage
    {
        get
        {
            var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
            var installed = Velopack.Locators.VelopackLocator.IsCurrentSet
                && Velopack.Locators.VelopackLocator.Current.CurrentlyInstalledVersion != null;
            return IsTestBuild(metadata.GetValueOrDefault("UpdateConfiguration") ?? "Local",
                metadata.GetValueOrDefault("ReleaseVersion"), installed);
        }
    }

    // Local packages (including portable/versioned preparation) and all RemoteTest builds
    // fail closed before SQLite, relocation, artwork or mutex work can use the personal profile.
    internal static bool IsTestBuild(string configuration, string? releaseVersion, bool installed) =>
        configuration == "RemoteTest" || configuration == "Local" && (installed || !string.IsNullOrEmpty(releaseVersion));

    internal static void EnsureTestDataIsolation() => _ = AppDataPaths.DirectoryPath;

    internal static void ValidateDataDirectory(string configuration, string? path, params string[] productionDirectories)
    {
        if (configuration != "RemoteTest") return;
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Remote test builds require an absolute isolated data directory before database startup.");
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var productionDirectory in productionDirectories)
        {
            var production = Path.TrimEndingDirectorySeparator(Path.GetFullPath(productionDirectory));
            if (data.Equals(production, StringComparison.OrdinalIgnoreCase)
                || data.StartsWith(production + ".preparing-", StringComparison.OrdinalIgnoreCase)
                || data.StartsWith(production + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Remote test builds cannot use production storage or its backups.");
        }
    }

    internal static IUpdateSource FromAssembly()
    {
        var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        return CreateSource(metadata.GetValueOrDefault("UpdateConfiguration") ?? "Local",
            metadata.GetValueOrDefault("UpdateRepository") ?? "",
            Environment.GetEnvironmentVariable(ApplicationUpdates.LocalSourceVariable), AppDataPaths.HasOverride);
    }

    internal static IUpdateSource CreateSource(string configuration, string repository, string? localPath, bool isolated)
    {
        if (configuration == "Local")
        {
            if (repository != "" || !isolated || string.IsNullOrEmpty(localPath) || !Path.IsPathFullyQualified(localPath))
                throw new InvalidOperationException("Local updates require an absolute local feed and an explicit isolated data directory.");
            return new SimpleFileSource(new DirectoryInfo(Path.GetFullPath(localPath)));
        }
        if (configuration == "Production")
        {
            if (repository != ProductionRepository)
                throw new InvalidOperationException("Production updates must use the official repository.");
        }
        else if (configuration == "RemoteTest")
        {
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z")
                || repository.Equals(ProductionRepository, StringComparison.OrdinalIgnoreCase)
                || repository.Equals(FormerProductionRepository, StringComparison.OrdinalIgnoreCase)
                || repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Remote tests require a separate GitHub owner/repository.");
            if (!isolated) throw new InvalidOperationException("Remote test updates require an explicit isolated data directory.");
        }
        else throw new InvalidOperationException("Unknown application update configuration.");
        // Local/test environment variables cannot redirect either compiled GitHub configuration.
        return new GithubSource("https://github.com/" + repository, null, false);
    }
}
