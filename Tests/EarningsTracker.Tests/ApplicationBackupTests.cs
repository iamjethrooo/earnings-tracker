using System.IO;
using System.Security.Cryptography;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Tests;

public sealed class ApplicationBackupTests
{
    private static DbContextOptions<AppDbContext> OptionsFor(string path) => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options;
    private sealed class Profile : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "earnings-backup-test-" + Guid.NewGuid().ToString("N"));
        internal string Data => Path.Combine(Root, "data");
        internal string Database => Path.Combine(Data, AppDataPaths.DatabaseFileName);
        internal DbContextOptions<AppDbContext> Options => OptionsFor(Database);
        internal Profile()
        {
            Directory.CreateDirectory(Data);
            using var db = new AppDbContext(Options);
            db.Database.Migrate();
            db.Games.Add(new Game { Name = "Synthetic game", RatePerHour = 1.2345m });
            db.SaveChanges();
            db.Sessions.Add(new Session { GameId = db.Games.Single().Id, GameName = "Synthetic game", Earnings = 2.3456m });
            CatalogService.SetSetting(db, "updates.automatic", "false");
            db.SaveChanges();
            db.SessionChanges.Add(new SessionChange { SessionId = db.Sessions.Single().Id,
                ChangedAt = new DateTime(2026, 10, 17), ChangeType = "Synthetic audit", AfterValuesJson = "{\"amount\":2.3456}" });
            db.SaveChanges();
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void BackupAndRestorePreserveDatabaseAndRecoverReplacedRecords()
    {
        using var profile = new Profile();
        var backup = Path.Combine(profile.Root, "user-backup.db");
        ApplicationBackup.Create(profile.Data, backup);
        ApplicationBackup.Validate(backup);
        using (var db = new AppDbContext(profile.Options))
        {
            db.Games.Add(new Game { Name = "Later record" });
            db.SaveChanges();
        }
        ApplicationBackup.ScheduleRestore(profile.Data, backup);
        var recovery = ApplicationBackup.ApplyPendingRestore(profile.Data);
        Assert.NotNull(recovery);
        using (var restored = new AppDbContext(profile.Options))
        {
            Assert.Equal("Synthetic game", Assert.Single(restored.Games).Name);
            Assert.Equal(2.3456m, Assert.Single(restored.Sessions).Earnings);
            Assert.Equal("Synthetic audit", Assert.Single(restored.SessionChanges).ChangeType);
            Assert.Equal("false", CatalogService.GetSetting(restored, "updates.automatic"));
            Assert.Single(restored.Database.GetAppliedMigrations());
        }
        using (var prior = new AppDbContext(OptionsFor(recovery!)))
            Assert.Equal(2, prior.Games.Count());
        Assert.False(Directory.Exists(Path.Combine(profile.Data, "Backups", "PendingRestore")));
    }

    [Fact]
    public void BackupIncludesCommittedWalAndDoesNotOverwriteAnExistingFile()
    {
        using var profile = new Profile();
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = profile.Database, Pooling = false }.ToString());
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL"; command.ExecuteScalar();
            command.CommandText = "INSERT INTO Games (Name, RatePerHour, MinutesCap, IsArchived) VALUES ('Committed WAL game', '1', '0', 0)";
            command.ExecuteNonQuery();
        }
        var backup = Path.Combine(profile.Root, "wal.db");
        ApplicationBackup.Create(profile.Data, backup);
        using var read = new AppDbContext(OptionsFor(backup));
        Assert.Contains(read.Games, game => game.Name == "Committed WAL game");
        var hash = SHA256.HashData(File.ReadAllBytes(backup));
        Assert.Throws<IOException>(() => ApplicationBackup.Create(profile.Data, backup));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(backup)));
    }

    [Fact]
    public void InvalidAndIncompatibleBackupsCannotBeScheduled()
    {
        using var profile = new Profile();
        var invalid = Path.Combine(profile.Root, "invalid.db");
        File.WriteAllText(invalid, "not SQLite");
        Assert.ThrowsAny<Exception>(() => ApplicationBackup.ScheduleRestore(profile.Data, invalid));
        var incompatible = Path.Combine(profile.Root, "future.db");
        ApplicationBackup.Create(profile.Data, incompatible);
        using (var db = new AppDbContext(OptionsFor(incompatible)))
            db.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsHistory VALUES ('99999999999999_Future', '10.0.0')");
        Assert.Throws<InvalidDataException>(() => ApplicationBackup.ScheduleRestore(profile.Data, incompatible));
        Assert.False(Directory.Exists(Path.Combine(profile.Data, "Backups", "PendingRestore")));
        using var current = new AppDbContext(profile.Options);
        Assert.Single(current.Games);
    }

    [Fact]
    public void FailedRecoveryProtectionKeepsCurrentDataAndPendingRestore()
    {
        using var profile = new Profile();
        var backup = Path.Combine(profile.Root, "backup.db");
        ApplicationBackup.Create(profile.Data, backup);
        ApplicationBackup.ScheduleRestore(profile.Data, backup);
        File.WriteAllText(Path.Combine(profile.Data, "Backups", "BeforeUserRestore"), "blocked by synthetic file");
        Assert.Throws<IOException>(() => ApplicationBackup.ApplyPendingRestore(profile.Data));
        using var current = new AppDbContext(profile.Options);
        Assert.Single(current.Games);
        Assert.True(Directory.Exists(Path.Combine(profile.Data, "Backups", "PendingRestore")));
    }

    [Fact]
    public void InterruptedRestoreRetriesOnlyWhenCurrentStillMatchesRecovery()
    {
        using var profile = new Profile();
        var backup = Path.Combine(profile.Root, "backup.db");
        ApplicationBackup.Create(profile.Data, backup);
        using (var db = new AppDbContext(profile.Options)) { db.Games.Add(new Game { Name = "Later" }); db.SaveChanges(); }
        ApplicationBackup.ScheduleRestore(profile.Data, backup);
        var pending = Path.Combine(profile.Data, "Backups", "PendingRestore");
        var original = Path.Combine(profile.Root, "original.db");
        DatabaseInitialization.CopyVerified(profile.Database, original);
        File.WriteAllText(Path.Combine(pending, "Applying.txt"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))));
        Assert.NotNull(ApplicationBackup.ApplyPendingRestore(profile.Data));
        using var read = new AppDbContext(profile.Options);
        Assert.Single(read.Games);
    }

    [Fact]
    public void LockedCurrentDatabaseLeavesPendingBackupAndCanRetry()
    {
        using var profile = new Profile();
        var backup = Path.Combine(profile.Root, "backup.db");
        ApplicationBackup.Create(profile.Data, backup);
        ApplicationBackup.ScheduleRestore(profile.Data, backup);
        using (var locked = new FileStream(profile.Database, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<Exception>(() => ApplicationBackup.ApplyPendingRestore(profile.Data));
        Assert.True(Directory.Exists(Path.Combine(profile.Data, "Backups", "PendingRestore")));
        Assert.NotNull(ApplicationBackup.ApplyPendingRestore(profile.Data));
    }

    [Fact]
    public void InterruptedAfterReplacementRecognizesCompletedRestore()
    {
        using var profile = new Profile();
        var backup = Path.Combine(profile.Root, "backup.db");
        ApplicationBackup.Create(profile.Data, backup);
        using (var db = new AppDbContext(profile.Options)) { db.Games.Add(new Game { Name = "Later" }); db.SaveChanges(); }
        ApplicationBackup.ScheduleRestore(profile.Data, backup);
        var pending = Path.Combine(profile.Data, "Backups", "PendingRestore");
        var original = Path.Combine(profile.Root, "original.db");
        DatabaseInitialization.CopyVerified(profile.Database, original);
        File.WriteAllText(Path.Combine(pending, "Applying.txt"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))));
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(pending, "Staged.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = profile.Database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            source.Open(); destination.Open(); source.BackupDatabase(destination);
        }
        Assert.NotNull(ApplicationBackup.ApplyPendingRestore(profile.Data));
        Assert.False(Directory.Exists(pending));
        using var restored = new AppDbContext(profile.Options);
        Assert.Single(restored.Games);
    }
}
