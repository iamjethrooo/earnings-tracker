using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using System.IO;

namespace EarningsTracker.Tests;

public class CleanInstallationTests
{
    [Fact]
    public void FileBackedBaselineCreatesOnlyCatalogAndDefaultsAcrossReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-clean-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "clean.db");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder
        { DataSource = path, ForeignKeys = true, Pooling = false }.ToString()).Options;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var db = new AppDbContext(options);
                db.Database.Migrate();
                DbInitializer.Initialize(db);
                CatalogService.ImportBundled(db);
                AppSettings.InitializeDefaults(db);
                Assert.EndsWith("_InitialCreate", Assert.Single(db.Database.GetAppliedMigrations()));
                Assert.False(db.Database.HasPendingModelChanges());
                Assert.Empty(db.Database.GetPendingMigrations());
                Assert.Equal(579, db.Games.Count());
                Assert.NotEmpty(db.GameMetadata);
                Assert.NotEmpty(db.GameGenres);
                Assert.NotEmpty(db.StoreOffers);
                Assert.NotEmpty(db.SubscriptionAvailability);
                Assert.All(db.Games, game => Assert.Null(game.CapResetDate));
                Assert.Empty(db.Sessions);
                Assert.Empty(db.Payouts.IgnoreQueryFilters());
                Assert.Empty(db.Bookmarks);
                Assert.Empty(db.SessionChanges);
                Assert.Empty(db.PayoutChanges);
                Assert.Empty(db.GameChanges);
                Assert.Empty(db.GameOwnership);
                Assert.Empty(db.ExchangeRates);
                Assert.Equal("true", CatalogService.GetSetting(db, "updates.automatic"));
                Assert.Equal("false", CatalogService.GetSetting(db, "subscription.PC Game Pass"));
                Assert.NotEmpty(CatalogService.GetSetting(db, "catalog.version"));
                Assert.NotEmpty(CatalogService.GetSetting(db, "catalog.published"));
                Assert.Equal(new[] { "catalog.published", "catalog.version", "subscription.PC Game Pass", "updates.automatic" },
                    db.CatalogSettings.Select(setting => setting.Key).OrderBy(key => key).ToArray());
                var totals = PayoutAccounting.Calculate(db.Sessions.ToList(), db.Payouts.ToList());
                Assert.Equal(0m, totals.TotalEarned);
                Assert.Equal(0m, totals.TotalPaidOut);
                Assert.Equal(0m, totals.Balance);
                Assert.Equal(0, totals.UndatedVerifiedSessions);
            }
        }
        finally
        {
            // Only this test's GUID-named directory and explicitly isolated database are removed.
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SQLiteBackupAndRecoveryIncludeCommittedWalRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-backup-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var backupPath = Path.Combine(directory, "backup.db");
        var restoredPath = Path.Combine(directory, "restored.db");
        string Connection(string path) => new SqliteConnectionStringBuilder
        { DataSource = path, ForeignKeys = true, Pooling = false }.ToString();
        try
        {
            using (var source = new SqliteConnection(Connection(sourcePath)))
            {
                source.Open();
                using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(source).Options);
                db.Database.Migrate();
                db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
                db.Sessions.Add(new Session { GameName = "Synthetic backup recording", Earnings = 1.2345m,
                    Status = Enums.SessionStatus.Verified, IsForceVerified = true, PostedDate = null });
                db.SaveChanges();
                Assert.True(new FileInfo(sourcePath + "-wal").Length > 0);
                using var backup = new SqliteConnection(Connection(backupPath));
                backup.Open(); source.BackupDatabase(backup);
            }
            using (var backup = new SqliteConnection(Connection(backupPath)))
            using (var restored = new SqliteConnection(Connection(restoredPath)))
            {
                backup.Open(); restored.Open(); backup.BackupDatabase(restored);
            }
            using var reopened = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(Connection(restoredPath)).Options);
            var recording = Assert.Single(reopened.Sessions);
            Assert.Equal("Synthetic backup recording", recording.GameName);
            Assert.Equal(1.2345m, recording.Earnings);
            Assert.True(recording.IsForceVerified);
            Assert.Null(recording.PostedDate);
            Assert.EndsWith("_InitialCreate", Assert.Single(reopened.Database.GetAppliedMigrations()));
            Assert.False(reopened.Database.HasPendingModelChanges());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DefaultInitializationDoesNotOverwritePreferencesOrInventAttempts()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        CatalogService.SetSetting(db, "updates.automatic", "false");
        CatalogService.SetSetting(db, "subscription.PC Game Pass", "true");
        CatalogService.SetSetting(db, "updates.lastAttempt", "2026-01-01T00:00:00Z");
        db.SaveChanges();
        AppSettings.InitializeDefaults(db);
        AppSettings.InitializeDefaults(db);
        Assert.Equal("false", CatalogService.GetSetting(db, "updates.automatic"));
        Assert.Equal("true", CatalogService.GetSetting(db, "subscription.PC Game Pass"));
        Assert.Equal("2026-01-01T00:00:00Z", CatalogService.GetSetting(db, "updates.lastAttempt"));
        Assert.Equal(3, db.CatalogSettings.Count());
    }

    [Fact]
    public void BaselinePreservesRequiredTablesIndexesAndForeignKeyActions()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var connection = db.Database.GetDbConnection();
        string[] Names(string sql)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names.ToArray();
        }
        Assert.Equal(new[] { "Bookmarks", "CatalogSettings", "ExchangeRates", "GameChanges", "GameGenres", "GameMetadata", "GameOwnership",
            "Games", "PayoutChanges", "Payouts", "SessionChanges", "Sessions", "StoreOffers", "SubscriptionAvailability" },
            Names("SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name"));
        Assert.Equal(new[] { "IX_Bookmarks_GameId", "IX_GameChanges_GameId", "IX_GameMetadata_CatalogId", "IX_PayoutChanges_PayoutId",
            "IX_Payouts_SourceId", "IX_SessionChanges_SessionId", "IX_Sessions_GameId", "IX_Sessions_PayoutId", "IX_StoreOffers_GameId",
            "IX_SubscriptionAvailability_GameId" }, Names("SELECT name FROM sqlite_schema WHERE type='index' AND name LIKE 'IX_%' ORDER BY name"));
        Assert.Equal(new[] { "RESTRICT", "SET NULL" }, Names("SELECT on_delete FROM pragma_foreign_key_list('Sessions') ORDER BY on_delete"));
        Assert.Equal(new[] { "RESTRICT" }, Names("SELECT on_delete FROM pragma_foreign_key_list('PayoutChanges')"));
        Assert.Equal(new[] { "CASCADE" }, Names("SELECT on_delete FROM pragma_foreign_key_list('Bookmarks')"));
        Assert.Empty(Names("SELECT [table] FROM pragma_foreign_key_list('SessionChanges')"));
        Assert.Empty(Names("SELECT [table] FROM pragma_foreign_key_list('GameChanges')"));
        Assert.Equal(new[] { "TEXT" }, Names("SELECT type FROM pragma_table_info('Sessions') WHERE name='Earnings'"));
        Assert.Equal(new[] { "GameId", "PostedDate" }, Names("SELECT name FROM pragma_table_info('Sessions') WHERE name IN ('GameId', 'PostedDate') AND [notnull]=0 ORDER BY name"));
        Assert.Equal(new[] { "SourceText" }, Names("SELECT name FROM pragma_table_info('SessionChanges') WHERE name='SourceText' AND [notnull]=0"));
        Assert.Equal(new[] { "IX_Bookmarks_GameId" }, Names("SELECT name FROM pragma_index_list('Bookmarks') WHERE [unique]=1"));
        Assert.Equal(new[] { "IX_GameMetadata_CatalogId" }, Names("SELECT name FROM pragma_index_list('GameMetadata') WHERE name='IX_GameMetadata_CatalogId' AND [unique]=1"));
        Assert.Equal(new[] { "ok" }, Names("PRAGMA integrity_check"));
        Assert.Empty(Names("PRAGMA foreign_key_check"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative.db")]
    [InlineData("C:\\temp\\nerve_earnings_tracker.db")]
    [InlineData("C:\\temp\\EarningsTracker.db")]
    public void DesignTimeFactoryRejectsMissingRelativeAndNormalDatabasePaths(string? path)
        => Assert.Throws<ArgumentException>(() => new AppDbContextFactory().CreateDbContext(path == null ? [] : ["--database", path]));

    [Fact]
    public void DesignTimeFactoryDoesNotCreateAnExplicitScratchDatabaseUntilUsed()
    {
        var path = Path.Combine(Path.GetTempPath(), "earnings-tracker-design-time-" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new AppDbContextFactory().CreateDbContext(["--database", path]);
        Assert.Equal(path, new SqliteConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString).DataSource);
        Assert.False(File.Exists(path));
    }
}
