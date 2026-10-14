using System.IO;
using System.Runtime.CompilerServices;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

internal static class TestStorageIsolation
{
    internal static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "earnings-tracker-tests-" + Guid.NewGuid().ToString("N"));

    // Covers incidental default contexts/artwork in rendered tests as well as explicit fixtures.
    // Always replace an inherited override: a test runner must never inherit a personal profile.
    [ModuleInitializer]
    internal static void Initialize() => Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable,
        DirectoryPath);
}
