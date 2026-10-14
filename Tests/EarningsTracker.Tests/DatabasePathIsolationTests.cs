using System.IO;
using EarningsTracker.Data;
using EarningsTracker.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

[Collection("DataChanged")]
public class DatabasePathIsolationTests(ITestOutputHelper output)
{
    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "synthetic-profile");

    [Theory]
    [InlineData(true, "EarningsTracker.Development")]
    [InlineData(false, "EarningsTracker")]
    public void BuildDefaultsAreSeparate(bool debug, string folder) =>
        Assert.Equal(Path.Combine(Profile, folder), AppDataPaths.ResolveDirectory(Profile, debug, null, false));

    [Fact]
    public void CompiledBuildSelectsExpectedDefaultAndLock()
    {
        var previous = Environment.GetEnvironmentVariable(AppDataPaths.OverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, null);
            if (ApplicationUpdateConfiguration.RequiresIsolatedStorage)
            {
                Assert.Throws<InvalidOperationException>(() => AppDataPaths.DirectoryPath);
                Assert.False(AppDataPaths.UsesProductionStorage);
                return;
            }
#if DEBUG
            Assert.True(AppDataPaths.IsDebugBuild);
            Assert.Equal(AppDataPaths.DevelopmentDirectoryPath, AppDataPaths.DirectoryPath);
            Assert.Equal(AppDataPaths.DevelopmentDirectoryPath, AppDataPaths.InstanceDirectoryPath);
            Assert.False(AppDataPaths.UsesProductionStorage);
#else
            Assert.False(AppDataPaths.IsDebugBuild);
            Assert.Equal(AppDataPaths.DefaultDirectoryPath, AppDataPaths.DirectoryPath);
            Assert.Equal(AppDataPaths.LegacyDirectoryPath, AppDataPaths.InstanceDirectoryPath);
            Assert.True(AppDataPaths.UsesProductionStorage);
#endif
        }
        finally { Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, previous); }
    }

    [Theory]
    [InlineData(true, "EarningsTracker")]
    [InlineData(true, "NerveEarningsTracker")]
    [InlineData(false, "EarningsTracker.Development")]
    public void CrossEnvironmentOverridesAndBackupsAreRejected(bool debug, string folder)
    {
        foreach (var path in new[] { Path.Combine(Profile, folder), Path.Combine(Profile, folder.ToUpperInvariant(), "Backups") })
            Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveDirectory(Profile, debug, path, false));
    }

    [Theory]
    [InlineData("Local", null, false, false)]
    [InlineData("Local", null, true, true)]
    [InlineData("Local", "0.0.1", false, true)]
    [InlineData("RemoteTest", null, false, true)]
    [InlineData("Production", "1.0.0", true, false)]
    public void InstalledAndPortableTestPackagesRequireIsolation(string configuration, string? version, bool installed, bool expected) =>
        Assert.Equal(expected, ApplicationUpdateConfiguration.IsTestBuild(configuration, version, installed));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestPackagesFailClosedWithoutScratchStorage(bool debug)
    {
        foreach (var path in new[] { null, "relative", Path.Combine(Profile, "EarningsTracker"),
            Path.Combine(Profile, "NerveEarningsTracker", "Backups"), Path.Combine(Profile, "EarningsTracker.Development") })
            Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveDirectory(Profile, debug, path, true));
        var scratch = Path.Combine(Profile, "scratch");
        Assert.Equal(scratch, AppDataPaths.ResolveDirectory(Profile, debug, scratch, true));
    }

    [Fact]
    public void AutomatedRunnerAlwaysStartsWithIsolatedStorage()
    {
        Assert.Equal(TestStorageIsolation.DirectoryPath, AppDataPaths.DirectoryPath);
        Assert.False(AppDataPaths.UsesProductionStorage);
        output.WriteLine("Automated runner directory: " + AppDataPaths.DirectoryPath);
        output.WriteLine("Runner default database: " + Path.Combine(AppDataPaths.DirectoryPath, AppDataPaths.DatabaseFileName));
    }

    [Fact]
    public void DefaultContextInScratchDoesNotImportLegacyAndInitializesOnlyBundles()
    {
        var previous = Environment.GetEnvironmentVariable(AppDataPaths.OverrideVariable);
        var scratch = Path.Combine(Path.GetTempPath(), "earnings-tracker-dev-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var legacy = Path.Combine(scratch, AppDataPaths.LegacyDatabaseFileName);
        File.WriteAllText(legacy, "preserved legacy personal database sentinel");
        try
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, scratch);
            using (var db = new AppDbContext())
            {
                Assert.False(AppDataPaths.UsesProductionStorage);
                Assert.Equal(scratch, AppDataPaths.InstanceDirectoryPath);
                Assert.Equal(Path.Combine(scratch, AppDataPaths.DatabaseFileName),
                    new SqliteConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString).DataSource);
                DatabaseInitialization.Initialize(db);
                CatalogService.ImportBundled(db);
                Assert.Equal(579, db.Games.Count());
                Assert.NotEmpty(db.GameMetadata);
                Assert.Empty(db.Sessions);
                Assert.Empty(db.Payouts.IgnoreQueryFilters());
                Assert.Empty(db.SessionChanges);
                Assert.Empty(db.PayoutChanges);
                Assert.Empty(db.GameChanges);
                Assert.Empty(db.Bookmarks);
                Assert.Empty(db.GameOwnership);
                Assert.Single(db.Database.GetAppliedMigrations());
                Assert.False(db.Database.HasPendingModelChanges());
                var totals = PayoutAccounting.Calculate(db.Sessions.ToList(), db.Payouts.ToList());
                Assert.Equal(0m, totals.TotalEarned);
                Assert.Equal(0m, totals.TotalPaidOut);
                Assert.Equal(0m, totals.Balance);
            }
            Assert.Equal("preserved legacy personal database sentinel", File.ReadAllText(legacy));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, previous);
            SqliteConnection.ClearAllPools();
            Directory.Delete(scratch, recursive: true); // Only this GUID-owned fixture.
        }
    }
}
