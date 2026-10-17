using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

public sealed class UserDataMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "earnings-tracker-separation-" + Guid.NewGuid().ToString("N"));
    private string Former => Path.Combine(root, "EarningsTracker");
    private string Destination => Path.Combine(root, "EarningsTracker.UserData");
    private static string Database(string directory) => Path.Combine(directory, AppDataPaths.DatabaseFileName);
    private readonly ITestOutputHelper output;
    public UserDataMigrationTests(ITestOutputHelper output)
    {
        this.output = output;
        Directory.CreateDirectory(root);
    }
    public void Dispose() => Directory.Delete(root, recursive: true); // Only this GUID-owned synthetic profile.

    private static void CreateDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = Database(directory), Pooling = false }.ToString()).Options);
        db.Database.Migrate();
        db.Games.Add(new Game { Name = "Synthetic preservation", RatePerHour = 1.2345m }); db.SaveChanges();
        var game = db.Games.Single();
        db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name, Earnings = 1.2345m,
            PostedDate = null, IsForceVerified = true, Status = Enums.SessionStatus.Verified });
        db.Payouts.Add(new Payout { Amount = -0.125m, Type = "Earnings correction", ReconciliationState = "Separate",
            SourceId = "synthetic-source", SourceRow = "original input", IsDeleted = true });
        db.SessionChanges.Add(new SessionChange { SessionId = 7, ChangeType = "Fixture", SourceText = "Original evidence" });
        db.GameChanges.Add(new GameChange { GameId = game.Id, ChangeType = "Fixture" });
        db.Bookmarks.Add(new Bookmark { GameId = game.Id });
        db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true, PurchaseAmount = 42.50m });
        db.CatalogSettings.Add(new CatalogSetting { Key = "updates.automatic", Value = "false" });
        db.SaveChanges();
        db.PayoutChanges.Add(new PayoutChange { PayoutId = db.Payouts.IgnoreQueryFilters().Single().Id, ChangeType = "Fixture" });
        db.SaveChanges();
    }

    // All rows/columns, including deleted payouts, audits, nullable evidence and EF history.
    private static string Fingerprint(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", command.ExecuteScalar());
        command.CommandText = "PRAGMA foreign_key_check";
        using (var violations = command.ExecuteReader()) Assert.False(violations.Read());
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        using (var history = command.ExecuteReader())
        {
            Assert.True(history.Read()); Assert.Equal("20261013055107_InitialCreate", history.GetString(0)); Assert.False(history.Read());
        }
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        var rows = new SortedDictionary<string, List<string>>();
        foreach (var table in tables)
        {
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
                values.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i))));
            values.Sort(StringComparer.Ordinal); rows.Add(table, values);
        }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData("EarningsTracker.db")]
    [InlineData("nerve_earnings_tracker.db")]
    public void CopiesDatabaseBackupsAndArtworkWithVerifiedRecoveryAndLeavesSourceIntact(string filename)
    {
        CreateDatabase(Former);
        var expected = Fingerprint(Database(Former));
        var bytes = File.ReadAllBytes(Database(Former));
        if (filename != AppDataPaths.DatabaseFileName) File.Move(Database(Former), Path.Combine(Former, filename));
        Directory.CreateDirectory(Path.Combine(Former, "current"));
        File.WriteAllText(Path.Combine(Former, "current", "EarningsTracker.exe"), "synthetic program sentinel");
        Directory.CreateDirectory(Path.Combine(Former, "Backups", "nested"));
        File.WriteAllBytes(Path.Combine(Former, "Backups", "nested", "saved.db"), bytes);
        Directory.CreateDirectory(Path.Combine(Former, "Artwork"));
        File.WriteAllText(Path.Combine(Former, "Artwork", "saved.img"), "cached artwork");
        UserDataMigration.Migrate(Destination, Former);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Former, filename)));
        Assert.Equal(expected, Fingerprint(Database(Destination)));
        Assert.Equal(expected, Fingerprint(Path.Combine(Destination, "Backups", "BeforeInstallSeparation", "Snapshot.db")));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Destination, "Backups", "nested", "saved.db")));
        Assert.Equal("cached artwork", File.ReadAllText(Path.Combine(Destination, "Artwork", "saved.img")));
        Assert.False(Directory.Exists(Path.Combine(Destination, "current")));
        output.WriteLine($"Source/working/recovery all-table fingerprint: {expected}; integrity=ok; FK violations=0; migration=20261013055107_InitialCreate");
        output.WriteLine($"Retained source/existing-backup SHA256: {Convert.ToHexString(SHA256.HashData(bytes))}");
        UserDataMigration.Migrate(Destination, Former); // Never re-import stale former data on reinstall.
        Assert.Equal(expected, Fingerprint(Database(Destination)));
    }

    [Fact]
    public void FrozenWalIsIncludedAndRecoveryWorksAfterSyntheticInstallFolderRemoval()
    {
        CreateDatabase(Former);
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database(Former), Pooling = false }.ToString()))
        {
            writer.Open(); using var command = writer.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; UPDATE Sessions SET Earnings='9.8765'"; command.ExecuteNonQuery();
            var frozen = Path.Combine(root, "frozen"); Directory.CreateDirectory(frozen);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Copy(Database(Former) + suffix, Database(frozen) + suffix);
            var expected = Fingerprint(Database(Former));
            UserDataMigration.Migrate(Destination, frozen);
            Assert.Equal(expected, Fingerprint(Database(Destination)));
        }
        var before = Fingerprint(Database(Destination));
        // Models filesystem scope only, not a native Velopack uninstall/repair rehearsal.
        Directory.Delete(Former, recursive: true);
        Assert.Equal(before, Fingerprint(Database(Destination)));
        var recovery = Path.Combine(Destination, "Backups", "BeforeInstallSeparation", "Snapshot.db");
        var recovered = Path.Combine(root, "recovered.db");
        DatabaseInitialization.CopyVerified(recovery, recovered);
        Assert.Equal(before, Fingerprint(recovered));
        output.WriteLine($"WAL/working/recovered all-table fingerprint after synthetic install-folder removal: {before}; integrity=ok; FK violations=0; migration=20261013055107_InitialCreate");
        UserDataMigration.Migrate(Destination, Former);
        Assert.Equal(before, Fingerprint(Database(Destination)));
    }

    [Fact]
    public void InvalidDatabaseRetainsSourceAndFailedPreparationThenCanRetry()
    {
        Directory.CreateDirectory(Former); File.WriteAllText(Database(Former), "invalid SQLite");
        Assert.Throws<SqliteException>(() => UserDataMigration.Migrate(Destination, Former));
        Assert.False(Directory.Exists(Destination)); Assert.Equal("invalid SQLite", File.ReadAllText(Database(Former)));
        Assert.Single(Directory.GetDirectories(root, "*.preparing-*"));
        File.Delete(Database(Former)); CreateDatabase(Former); // Repair only the disposable fixture.
        UserDataMigration.Migrate(Destination, Former);
        Assert.Equal(Fingerprint(Database(Former)), Fingerprint(Database(Destination)));
    }

    [Fact]
    public void OpenDatabaseFailsBeforePublishingAndRetryPreservesRows()
    {
        CreateDatabase(Former);
        using (var held = new FileStream(Database(Former), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => UserDataMigration.Migrate(Destination, Former));
        Assert.False(Directory.Exists(Destination));
        UserDataMigration.Migrate(Destination, Former);
        Assert.Equal(Fingerprint(Database(Former)), Fingerprint(Database(Destination)));
    }

    [Fact]
    public void ForeignKeyViolationsStopBeforePublishingAndKeepTheOriginalDatabase()
    {
        CreateDatabase(Former);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database(Former), Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=OFF; UPDATE Bookmarks SET GameId=987654"; command.ExecuteNonQuery();
        }
        var before = File.ReadAllBytes(Database(Former));
        Assert.Throws<InvalidDataException>(() => UserDataMigration.Migrate(Destination, Former));
        Assert.False(Directory.Exists(Destination));
        Assert.Equal(before, File.ReadAllBytes(Database(Former)));
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("orphan")]
    [InlineData("both-names")]
    [InlineData("both-locations")]
    [InlineData("both-sidecars")]
    public void AmbiguousOrIncompleteProfilesFailWithoutChangingTheSource(string state)
    {
        CreateDatabase(Former); var before = File.ReadAllBytes(Database(Former));
        var legacy = Path.Combine(root, "NerveEarningsTracker");
        if (state == "destination") Directory.CreateDirectory(Destination);
        if (state == "orphan") File.Move(Database(Former), Database(Former) + "-wal");
        if (state == "both-names") File.WriteAllText(Path.Combine(Former, AppDataPaths.LegacyDatabaseFileName), "preserved conflict");
        if (state == "both-locations") CreateDatabase(legacy);
        if (state == "both-sidecars")
            foreach (var name in new[] { AppDataPaths.DatabaseFileName, AppDataPaths.LegacyDatabaseFileName })
                File.WriteAllText(Path.Combine(Former, name) + "-wal", "conflicting sidecar");
        Assert.Throws<InvalidOperationException>(() => UserDataMigration.Migrate(Destination, Former, legacy));
        Assert.Equal(before, File.ReadAllBytes(Database(Former) + (state == "orphan" ? "-wal" : "")));
        Assert.False(File.Exists(Database(Destination)));
    }

    [Fact]
    public void FreshProfileAndAbsoluteNonoverlappingPathsAreRequired()
    {
        UserDataMigration.Migrate(Destination, Former);
        Assert.False(Directory.Exists(Destination));
        Assert.Throws<ArgumentException>(() => UserDataMigration.Migrate("relative", Former));
        Assert.Throws<InvalidOperationException>(() => UserDataMigration.Migrate(Path.Combine(Former, "data"), Former));
        Assert.Throws<InvalidOperationException>(() => UserDataMigration.Migrate(root, Former));
    }

    [Fact]
    public void LinkedBackupFoldersAreRejectedAndNoPartialProfileIsPublished()
    {
        CreateDatabase(Former);
        var target = Path.Combine(root, "linked-backups"); Directory.CreateDirectory(target);
        var link = Path.Combine(Former, "Backups");
        // Directory junctions need no developer-mode privilege on Windows.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        Assert.NotNull(process); process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.Throws<InvalidOperationException>(() => UserDataMigration.Migrate(Destination, Former));
            Assert.False(Directory.Exists(Destination));
            Assert.Empty(Directory.GetFiles(target));
        }
        finally { Directory.Delete(link); }
    }
}
