using System.IO;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

public class InstanceIdentityTests
{
    [Theory]
    [InlineData("scratch")]
    [InlineData("EarningsTracker.UserData")]
    public void EquivalentFolderSpellingsShareTheSameInstanceLock(string folder)
    {
        var root = Path.Combine(TestStorageIsolation.DirectoryPath, "identity");
        var path = Path.Combine(root, folder);
        var production = Path.Combine(root, "EarningsTracker.UserData");
        var legacy = Path.Combine(root, "NerveEarningsTracker");
        string Resolve(string directory) => AppDataPaths.ResolveInstanceDirectory(directory, production, legacy).ToUpperInvariant();
        Assert.Equal(Resolve(path), Resolve(path + Path.DirectorySeparatorChar));
        Assert.Equal(Resolve(path), Resolve(path.Replace('\\', '/')));
        Assert.Equal(Resolve(path), Resolve(Path.Combine(path, ".")));
        Assert.Equal(Resolve(path), Resolve(path.ToUpperInvariant()));
        if (folder == "EarningsTracker.UserData") Assert.Equal(legacy.ToUpperInvariant(), Resolve(path));
        else Assert.Equal(path.ToUpperInvariant(), Resolve(path));
    }

    [Fact]
    public void SeparateScratchProfilesKeepSeparateLocks()
    {
        var root = TestStorageIsolation.DirectoryPath;
        var production = Path.Combine(root, "production"); var legacy = Path.Combine(root, "legacy");
        Assert.NotEqual(AppDataPaths.ResolveInstanceDirectory(Path.Combine(root, "first"), production, legacy),
            AppDataPaths.ResolveInstanceDirectory(Path.Combine(root, "second"), production, legacy));
    }
}
