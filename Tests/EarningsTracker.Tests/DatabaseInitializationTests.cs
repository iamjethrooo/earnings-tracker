using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EarningsTracker.Tests;

[Collection("DataChanged")]
public class DatabaseInitializationTests
{
    [Fact]
    public void NormalInitializationPreservesPersonalRecordsAndPreferences()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var game = new Game { Name = "Synthetic existing game", RatePerHour = 2.5m, CapResetDate = new DateTime(2026, 6, 1) };
        db.Games.Add(game); db.SaveChanges();
        db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name, Earnings = 1.2345m });
        db.Bookmarks.Add(new Bookmark { GameId = game.Id });
        db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true, PurchaseAmount = 10.50m });
        CatalogService.SetSetting(db, "updates.automatic", "false");
        CatalogService.SetSetting(db, "subscription.PC Game Pass", "true");
        db.SaveChanges();
        PayoutService.Create(new Payout { Date = new DateTime(2026, 6, 5), Type = "Earnings correction",
            Description = "Synthetic signed correction", Status = "Verified", Amount = -1.23m, ReconciliationState = "Separate" }, options: database.Options);
        for (var attempt = 0; attempt < 2; attempt++) DatabaseInitialization.Initialize(db);
        using var read = database.CreateContext();
        Assert.Equal(game.CapResetDate, read.Games.Single(saved => saved.Id == game.Id).CapResetDate);
        Assert.Equal(1.2345m, Assert.Single(read.Sessions).Earnings);
        Assert.Equal(-1.23m, Assert.Single(read.Payouts).Amount);
        Assert.Single(read.PayoutChanges);
        Assert.Single(read.Bookmarks);
        Assert.Equal(10.50m, Assert.Single(read.GameOwnership).PurchaseAmount);
        Assert.Equal("false", CatalogService.GetSetting(read, "updates.automatic"));
        Assert.Equal("true", CatalogService.GetSetting(read, "subscription.PC Game Pass"));
    }

    [Fact]
    public void ExistingProfileReceivesBundledGamesAndAnAuditedRateUpdateOnlyOnce()
    {
        using var database = new TestDatabase();
        int predecessorId;
        var capReset = new DateTime(2026, 10, 1);
        using (var db = database.CreateContext())
        {
            var predecessor = new Game { Name = "predecessor", RatePerHour = 1.5m, MinutesCap = 3000m, CapResetDate = capReset };
            db.Sessions.Add(new Session { Game = predecessor, GameName = "Predecessor", Earnings = 1.2345m });
            db.SaveChanges();
            predecessorId = predecessor.Id;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var db = database.CreateContext();
            DatabaseInitialization.Initialize(db);
            CatalogService.ImportBundled(db);
        }

        using var read = database.CreateContext();
        Assert.Equal(3, read.Games.Count());
        Assert.All(read.Games, game =>
        {
            Assert.False(game.IsArchived);
            Assert.Equal(2m, game.RatePerHour);
            Assert.Equal(3000m, game.MinutesCap);
        });
        Assert.Equal(new[] { "MAVRIX", "Wreckreation", "predecessor" }, read.Games.OrderBy(game => game.Name).Select(game => game.Name).ToArray());
        Assert.Equal(3, read.GameMetadata.Count());
        Assert.Equal(capReset, read.Games.Single(game => game.Id == predecessorId).CapResetDate);
        var session = Assert.Single(read.Sessions);
        Assert.Equal(predecessorId, session.GameId);
        Assert.Equal(1.2345m, session.Earnings);
        var audit = Assert.Single(read.GameChanges);
        Assert.Equal(predecessorId, audit.GameId);
        Assert.Equal("Bundled rate changed", audit.ChangeType);
        using var before = JsonDocument.Parse(audit.BeforeValuesJson);
        using var after = JsonDocument.Parse(audit.AfterValuesJson);
        Assert.Equal(1.5m, before.RootElement.GetProperty("RatePerHour").GetDecimal());
        Assert.Equal(2m, after.RootElement.GetProperty("RatePerHour").GetDecimal());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RosterUpdatePreservesCustomizedAndAmbiguousEntries(bool duplicatePredecessor)
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        var mavrix = new Game { Name = "mavrix", RatePerHour = 3m, MinutesCap = 60m, IsArchived = true };
        var predecessor = new Game { Name = "Predecessor", RatePerHour = 1.5m, MinutesCap = duplicatePredecessor ? 3000m : 60m };
        db.Games.AddRange(mavrix, predecessor);
        if (duplicatePredecessor)
            db.Games.Add(new Game { Name = "PREDECESSOR", RatePerHour = 1m, MinutesCap = 120m, IsArchived = true });
        db.SaveChanges();

        DatabaseInitialization.Initialize(db);

        using var read = database.CreateContext();
        Assert.Equal(duplicatePredecessor ? 4 : 3, read.Games.Count());
        var savedMavrix = read.Games.Single(game => game.Id == mavrix.Id);
        Assert.Equal(3m, savedMavrix.RatePerHour);
        Assert.Equal(60m, savedMavrix.MinutesCap);
        Assert.True(savedMavrix.IsArchived);
        Assert.Equal(1.5m, read.Games.Single(game => game.Id == predecessor.Id).RatePerHour);
        Assert.Empty(read.GameChanges);
    }

    [Fact]
    public void FailedRosterAuditRollsBackNewGamesAndRateChange()
    {
        using var database = new TestDatabase();
        using (var db = database.CreateContext())
        {
            db.Games.Add(new Game { Name = "Predecessor", RatePerHour = 1.5m, MinutesCap = 3000m });
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectRosterAudit BEFORE INSERT ON GameChanges BEGIN SELECT RAISE(ABORT, 'test roster audit failure'); END;");
            Assert.Throws<DbUpdateException>(() => DatabaseInitialization.Initialize(db));
        }

        using (var read = database.CreateContext())
        {
            Assert.Equal(1.5m, Assert.Single(read.Games).RatePerHour);
            Assert.Empty(read.GameChanges);
            read.Database.ExecuteSqlRaw("DROP TRIGGER RejectRosterAudit");
        }

        using var retry = database.CreateContext();
        DatabaseInitialization.Initialize(retry);
        Assert.Equal(3, retry.Games.Count());
        Assert.Single(retry.GameChanges);
    }

    [Fact]
    public void UnsupportedMigrationHistoryFailsWithoutChangingRecordsOrHistory()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Preserved" }); db.SaveChanges();
        db.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsHistory VALUES ('20260907172227_InitialCreate', '10.0.11')");
        var history = db.Database.GetAppliedMigrations().ToArray();
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseInitialization.Initialize(db));
        Assert.Contains("has not been reset", error.Message);
        Assert.Equal(history, db.Database.GetAppliedMigrations());
        Assert.Equal("Preserved", Assert.Single(db.Games).Name);
        Assert.Empty(db.CatalogSettings);
    }

    [Fact]
    public void ExistingTablesWithoutHistoryAreNotRecreatedOrSeeded()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.ExecuteSqlRaw("CREATE TABLE PersonalLegacy (Evidence TEXT); INSERT INTO PersonalLegacy VALUES ('synthetic evidence')");
        Assert.Throws<InvalidOperationException>(() => DatabaseInitialization.Initialize(db));
        using var command = connection.CreateCommand(); command.CommandText = "SELECT Evidence FROM PersonalLegacy";
        Assert.Equal("synthetic evidence", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name='Games'";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void InterruptedMigrationLockFailsBeforeSeedingAndPreservesLockAndHistory()
    {
        using var database = new TestDatabase();
        using var db = database.CreateContext();
        db.Games.Add(new Game { Name = "Preserved after interrupted upgrade" });
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsLock (Id, Timestamp) VALUES (1, '2026-10-17T00:00:00Z')");
        var history = db.Database.GetAppliedMigrations().ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => DatabaseInitialization.Initialize(db));

        Assert.Contains("migration lock", error.Message);
        Assert.Contains("reviewed recovery", error.Message);
        Assert.Equal(history, db.Database.GetAppliedMigrations());
        Assert.Equal("Preserved after interrupted upgrade", Assert.Single(db.Games).Name);
        Assert.Empty(db.CatalogSettings);
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT Timestamp FROM __EFMigrationsLock WHERE Id=1";
        Assert.Equal("2026-10-17T00:00:00Z", command.ExecuteScalar());
    }

    [Fact]
    public void LegacyCopyIncludesWalAndDoesNotReplaceAnExistingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-relocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var destination = Path.Combine(directory, "destination.db");
        try
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Pooling = false }.ToString());
            source.Open();
            using var command = source.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Evidence (Value TEXT); INSERT INTO Evidence VALUES ('committed WAL')";
            command.ExecuteNonQuery();
            DatabaseInitialization.CopyLegacyIfMissing(destination, sourcePath);
            using (var copied = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()))
            {
                copied.Open(); using var read = copied.CreateCommand(); read.CommandText = "SELECT Value FROM Evidence";
                Assert.Equal("committed WAL", read.ExecuteScalar());
            }
            var before = SHA256.HashData(File.ReadAllBytes(destination));
            command.CommandText = "INSERT INTO Evidence VALUES ('later source record')"; command.ExecuteNonQuery();
            DatabaseInitialization.CopyLegacyIfMissing(destination, sourcePath);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(destination)));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FailedLegacyCopyLeavesNoDestinationAndCanBeRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-relocation-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.db"); var destination = Path.Combine(directory, "destination.db");
        try
        {
            File.WriteAllText(source, "not a SQLite database");
            Assert.Throws<SqliteException>(() => DatabaseInitialization.CopyLegacyIfMissing(destination, source));
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Equal("not a SQLite database", File.ReadAllText(source));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void IsolatedDirectoryIsQuotedAsAPathRatherThanConnectionOptions()
    {
        var previous = Environment.GetEnvironmentVariable(AppDataPaths.OverrideVariable);
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-path-" + Guid.NewGuid().ToString("N") + ";Mode=ReadOnly");
        try
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, directory);
            using var db = new AppDbContext();
            var connection = new SqliteConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
            Assert.Equal(Path.Combine(directory, AppDataPaths.DatabaseFileName), connection.DataSource);
            Assert.Equal(SqliteOpenMode.ReadWriteCreate, connection.Mode);
            Assert.False(File.Exists(connection.DataSource));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, previous);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void IsolatedDataOverrideDoesNotFallBackToUserStorage()
    {
        var previous = Environment.GetEnvironmentVariable(AppDataPaths.OverrideVariable);
        var directory = Path.Combine(Path.GetTempPath(), "earnings-tracker-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, directory);
            Assert.True(AppDataPaths.HasOverride);
            Assert.Equal(directory, AppDataPaths.DirectoryPath);
            Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, "relative/path");
            Assert.Throws<InvalidOperationException>(() => AppDataPaths.DirectoryPath);
            Assert.False(Directory.Exists(directory));
        }
        finally { Environment.SetEnvironmentVariable(AppDataPaths.OverrideVariable, previous); }
    }
}
