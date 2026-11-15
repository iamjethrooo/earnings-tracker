using System.IO;
using System.Text.Json;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

public class LocalInstallationsTests
{
    [Fact]
    public void WindowsRegistrationsAreOptInAndFolderEvidenceStaysSeparate()
    {
        var root = Directory.CreateTempSubdirectory("earnings-local-install-");
        try
        {
            var registered = Directory.CreateDirectory(Path.Combine(root.FullName, "Registered Game"));
            var portable = Directory.CreateDirectory(Path.Combine(root.FullName, "Portable Game"));
            var folders = JsonSerializer.Serialize(new[] { portable.FullName, portable.FullName, @"\\server\share\Game" });
            var disabled = LocalGameInstallations.Read("", "", false, () => throw new InvalidOperationException());
            Assert.False(disabled.IsConfigured);
            Assert.Empty(disabled.Entries);

            var installations = LocalGameInstallations.Read("", folders, true, () =>
            new([
                new("Registered Game", null, false),
                new("Registered Game", registered.FullName, false),
                new("Registration only", Path.Combine(root.FullName, "Missing"), false),
                new("Hidden component", registered.FullName, true),
                new(null, registered.FullName, false)
            ], false));
            Assert.True(installations.IsConfigured);
            Assert.Empty(installations.IncompleteSources);
            Assert.Null(installations.SteamAppIds);
            Assert.Equal(new[] { "Portable Game", "Registered Game", "Registration only" },
                installations.Entries.Select(entry => entry.Name));
            Assert.Equal(LocalGameInstallations.FolderSource, installations.Entries[0].Source);
            Assert.True(installations.Entries[1].HasLocalFolder);
            Assert.False(installations.Entries[2].HasLocalFolder);
            Assert.Contains("registration only", installations.Entries[2].Evidence);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void PartialSourcesAreReportedAlongsideUsableResults()
    {
        var root = Directory.CreateTempSubdirectory("earnings-partial-install-");
        try
        {
            var steam = JsonSerializer.Serialize(new[] { Path.Combine(root.FullName, "steamapps") });
            var folders = JsonSerializer.Serialize(new[] { Path.Combine(root.FullName, "Unavailable") });
            var scan = LocalGameInstallations.Read(steam, folders, true,
                () => new([new("Available registration", null, false)], true));

            Assert.Equal(new[] { LocalGameInstallations.SteamSource, LocalGameInstallations.FolderSource,
                LocalGameInstallations.WindowsSource }, scan.IncompleteSources);
            Assert.Equal("Available registration", Assert.Single(scan.Entries).Name);
        }
        finally { root.Delete(recursive: true); }
    }
}
