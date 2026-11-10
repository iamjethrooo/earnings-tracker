using System.IO;
using System.Text.Json;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

public class SteamInstallationTests
{
    [Fact]
    public void ReadsOnlyValidInstalledManifestsInSelectedLibraries()
    {
        var root = Directory.CreateTempSubdirectory("earnings-steam-fixture-");
        try
        {
            var first = Path.Combine(root.FullName, "first", "steamapps");
            var second = Path.Combine(root.FullName, "second", "steamapps");
            Directory.CreateDirectory(Path.Combine(first, "common", "Example"));
            Directory.CreateDirectory(Path.Combine(second, "common", "Example"));
            File.WriteAllText(Path.Combine(first, "appmanifest_123.acf"), Manifest(123, 4));
            File.WriteAllText(Path.Combine(second, "appmanifest_456.acf"), Manifest(456, 4));
            File.WriteAllText(Path.Combine(first, "appmanifest_111.acf"), Manifest(123, 4));
            File.WriteAllText(Path.Combine(second, "appmanifest_789.acf"), Manifest(789, 6));
            File.WriteAllText(Path.Combine(second, "appmanifest_222.acf"), Manifest(222, 4).Replace("Example", "Missing"));
            File.WriteAllText(Path.Combine(second, "appmanifest_333.acf"), "malformed");
            Assert.Null(SteamInstallations.Read(""));
            Assert.Empty(SteamInstallations.Read("malformed")!);
            Assert.Equal(new uint[] { 123, 456 }, SteamInstallations.Read(JsonSerializer.Serialize(new[] { first, second }))!.Order());
        }
        finally { root.Delete(recursive: true); }
    }

    private static string Manifest(uint id, int flags) => $$"""
        "AppState"
        {
            "appid" "{{id}}"
            "StateFlags" "{{flags}}"
            "installdir" "Example"
            "InstalledDepots"
            {
                "appid" "999"
            }
        }
        """;
}
