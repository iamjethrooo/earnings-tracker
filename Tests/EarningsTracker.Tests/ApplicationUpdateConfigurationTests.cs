using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using EarningsTracker.Utilities;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class ApplicationUpdateConfigurationTests(WpfTestHost host)
{
    [Fact]
    public void RemoteBuildRejectsProductionStorageBeforeDatabaseStartup()
    {
        var production = Path.Combine(Path.GetTempPath(), "NerveEarningsTracker");
        foreach (var path in new[] { null, "relative", production, Path.Combine(production,"Backups") })
            Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest",path,production));
        ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest",Path.Combine(Path.GetTempPath(),"Isolated-Nerve"),production);
        ApplicationUpdateConfiguration.ValidateDataDirectory("Local",null,production);
        ApplicationUpdateConfiguration.ValidateDataDirectory("Production",null,production);
    }
    [Fact]
    public void LocalSourceNeverFallsBackToProduction()
    {
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("Local", "", null, true));
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("Local", "", "relative", true));
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("Local", "", Path.GetTempPath(), false));
        Assert.IsType<SimpleFileSource>(ApplicationUpdateConfiguration.CreateSource("Local", "", Path.GetTempPath(), true));
    }

    [Theory]
    [InlineData("iamjethrooo/earnings-tracker")]
    [InlineData("IAMJETHROOO/NERVE-EARNINGS-TRACKER")]
    [InlineData("IAMJETHROOO/EARNINGS-TRACKER")]
    [InlineData("iamjethrooo/nerve-earnings-tracker")]
    [InlineData("https://github.com/test/updates")]
    [InlineData("test/updates?token=secret")]
    [InlineData("test/updates.git")]
    [InlineData("")]
    public void RemoteSourceRejectsProductionAndMalformedRepositories(string repository) =>
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("RemoteTest", repository, null, true));

    [Fact]
    public void CompiledGithubSourcesIgnoreLocalEnvironmentOverrides()
    {
        Assert.IsType<GithubSource>(ApplicationUpdateConfiguration.CreateSource("Production",
            ApplicationUpdateConfiguration.ProductionRepository, Path.GetTempPath(), true));
        Assert.IsType<GithubSource>(ApplicationUpdateConfiguration.CreateSource("RemoteTest", "test/updates", Path.GetTempPath(), true));
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("Production", "test/updates", null, false));
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("RemoteTest", "test/updates", null, false));
        Assert.Throws<InvalidOperationException>(() => ApplicationUpdateConfiguration.CreateSource("Unknown", "", null, false));
    }

    private sealed class GithubResponses : IFileDownloader
    {
        internal string Version = "1.0.1";
        internal string? BrokenFeed;
        internal bool Offline;
        internal readonly List<string> Requests = [];
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Requests.Add(url);
            Assert.False(headers?.Keys.Any(key => key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) == true);
            if (Offline) throw new HttpRequestException("Offline fixture");
            if (url.Contains("api.github.com"))
                return Task.FromResult(JsonSerializer.Serialize(new[] {
                    Release("catalog", true, "2099-01-01T00:00:00Z"),
                    Release("app", false, "2026-10-13T00:00:00Z") }));
            if (url.Contains("catalog")) throw new InvalidOperationException("Prerelease must be excluded");
            return Task.FromResult(BrokenFeed ?? JsonSerializer.Serialize(new { Assets = new[] {
                new { PackageId = "EarningsTracker.App", Version, Type = "Full", FileName = "app-full.nupkg", SHA1 = new string('A',40), SHA256 = new string('B',64), Size = 100 } } }));
        }
        private static object Release(string name, bool prerelease, string published) => new {
            id = prerelease ? 2 : 1, name, tag_name = name, prerelease, published_at = published,
            assets = new[] {
                new { name = "releases.win-x64-app-stable.json", browser_download_url = $"https://github.com/test/updates/releases/download/{name}/releases.win-x64-app-stable.json", url = $"https://api.github.com/assets/{name}-feed" },
                new { name = "app-full.nupkg", browser_download_url = $"https://github.com/test/updates/releases/download/{name}/app-full.nupkg", url = $"https://api.github.com/assets/{name}-package" } } };
        public async Task<byte[]> DownloadBytes(string url, IDictionary<string,string>? headers = null, double timeout = 30) =>
            Encoding.UTF8.GetBytes(await DownloadString(url, headers, timeout));
        public Task DownloadFile(string url, string targetFile, Action<int>? progress = null, IDictionary<string,string>? headers = null, double timeout = 30, CancellationToken cancelToken = default) =>
            throw new InvalidOperationException("Discovery must not automatically download");
    }

    private static UpdateManager Manager(GithubResponses responses) => new(new GithubSource("https://github.com/test/updates", null, false, responses),
        new UpdateOptions { ExplicitChannel = ApplicationUpdateConfiguration.Channel, AllowVersionDowngrade = false },
        new TestVelopackLocator("EarningsTracker.App", "1.0.0", Path.GetTempPath()));

    [Fact]
    public async Task PinnedSdkDiscoversStablePublicAssetsAndSkipsCatalogPrerelease()
    {
        var responses = new GithubResponses(); var update = await Manager(responses).CheckForUpdatesAsync();
        Assert.NotNull(update); Assert.Equal("1.0.1", update.TargetFullRelease.Version.ToString());
        Assert.Contains(responses.Requests, url => url.StartsWith("https://github.com/test/updates/releases/download/app/"));
        Assert.DoesNotContain(responses.Requests, url => url.Contains("/catalog/"));
    }

    [Theory]
    [InlineData("0.9.0")]
    [InlineData("1.0.0")]
    public async Task PinnedSdkDoesNotDowngradeOrReapplySameVersion(string version)
    {
        var responses = new GithubResponses { Version = version };
        Assert.Null(await Manager(responses).CheckForUpdatesAsync());
    }

    [Fact]
    public async Task PinnedSdkAllowsRetryAfterOfflineOrMalformedFeed()
    {
        var responses = new GithubResponses { Offline = true }; var manager = Manager(responses);
        await Assert.ThrowsAnyAsync<Exception>(() => manager.CheckForUpdatesAsync());
        responses.Offline = false; responses.BrokenFeed = "not-json";
        await Assert.ThrowsAnyAsync<Exception>(() => manager.CheckForUpdatesAsync());
        responses.BrokenFeed = null; Assert.NotNull(await manager.CheckForUpdatesAsync());
    }

    [Fact]
    public Task StableUiNeverOffersAPrereleasePackageEvenIfGithubReleaseIsMislabelled() => host.RunAsync(async () =>
    {
        var responses = new GithubResponses { Version = "1.0.2-beta.1" };
        using var updates = new ApplicationUpdates(Manager(responses), System.Windows.Threading.Dispatcher.CurrentDispatcher,
            new UpdateRestartGuard(), () => false, () => () => { }, () => { });
        await updates.CheckAsync();
        Assert.False(updates.CanDownload); Assert.False(updates.CanRestart);
        Assert.NotEqual(ApplicationUpdateState.Available, updates.State);
    });
}
