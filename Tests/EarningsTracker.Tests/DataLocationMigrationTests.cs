using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using System.IO;
using System.Security.Cryptography;

namespace EarningsTracker.Tests;

public sealed class DataLocationMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "earnings-tracker-location-" + Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(root, "NerveEarningsTracker");
    private string Current => Path.Combine(root, "EarningsTracker");
    private string OldDatabase(string directory) => Path.Combine(directory, AppDataPaths.LegacyDatabaseFileName);
    private string NewDatabase => Path.Combine(Current, AppDataPaths.DatabaseFileName);

    public DataLocationMigrationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true); // Only this GUID-owned isolated fixture.
    private static DbContextOptions<AppDbContext> Options(string path) => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()).Options;
    private void CreatePersonalDatabase(string directory)
    {
        Directory.CreateDirectory(directory);
        using var db = new AppDbContext(Options(OldDatabase(directory)));
        db.Database.Migrate();
        db.Games.Add(new Game { Name = "Location fixture", RatePerHour = 1.2345m }); db.SaveChanges();
        var game = db.Games.Single();
        db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name, Earnings = 1.2345m, PostedDate = null,
            IsForceVerified = true, Status = Enums.SessionStatus.Verified });
        db.Payouts.Add(new Payout { Amount = -0.125m, Type = "Earnings correction", ReconciliationState = "Separate", SourceId = "preserved-source", IsDeleted = true });
        db.SessionChanges.Add(new SessionChange { SessionId = 7, ChangeType = "Fixture", SourceText = "Original input" });
        db.Bookmarks.Add(new Bookmark { GameId = game.Id });
        db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true, PurchaseAmount = 42.50m });
        db.CatalogSettings.Add(new CatalogSetting { Key = "updates.automatic", Value = "false" });
        db.SaveChanges();
    }

    [Fact]
    public void MigrationMovesAllUserFilesAndPreservesDatabaseBytesAndRecoverySnapshot()
    {
        CreatePersonalDatabase(Legacy);
        Directory.CreateDirectory(Path.Combine(Legacy, "Artwork"));
        Directory.CreateDirectory(Path.Combine(Legacy, "Backups"));
        File.WriteAllText(Path.Combine(Legacy, "Artwork", "fixture.img"), "saved artwork");
        File.WriteAllText(Path.Combine(Legacy, "Backups", "retained.txt"), "retained backup");
        var original = SHA256.HashData(File.ReadAllBytes(OldDatabase(Legacy)));
        DataLocationMigration.Migrate(Legacy, Current);
        Assert.False(Directory.Exists(Legacy));
        Assert.False(File.Exists(OldDatabase(Current)));
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(NewDatabase)));
        Assert.Equal("saved artwork", File.ReadAllText(Path.Combine(Current,"Artwork","fixture.img")));
        Assert.Equal("retained backup", File.ReadAllText(Path.Combine(Current,"Backups","retained.txt")));
        var snapshot = Assert.Single(Directory.GetFiles(Path.Combine(Current,"Backups","BeforeDataLocationMigration"),"Snapshot.db",SearchOption.AllDirectories));
        using var db = new AppDbContext(Options(snapshot));
        Assert.Equal(1.2345m, Assert.Single(db.Sessions).Earnings);
        Assert.Null(db.Sessions.Single().PostedDate);
        Assert.Equal("preserved-source",db.Payouts.IgnoreQueryFilters().Single().SourceId);
        Assert.Equal(-0.125m,Assert.Single(db.Payouts.IgnoreQueryFilters()).Amount);
        Assert.Equal("Original input",Assert.Single(db.SessionChanges).SourceText);
        Assert.Single(db.Bookmarks);
        Assert.Equal(42.50m,Assert.Single(db.GameOwnership).PurchaseAmount);
        Assert.Equal("false",Assert.Single(db.CatalogSettings).Value);
        Assert.Equal("20261013055107_InitialCreate",Assert.Single(db.Database.GetAppliedMigrations()));
        Assert.False(db.Database.HasPendingModelChanges());
        DataLocationMigration.Migrate(Legacy, Current);
        Assert.Single(Directory.GetFiles(Path.Combine(Current,"Backups","BeforeDataLocationMigration"),"Snapshot.db",SearchOption.AllDirectories));
        Assert.Equal(original,SHA256.HashData(File.ReadAllBytes(NewDatabase)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingDestinationIsNeverReplaced(bool hasDatabase)
    {
        CreatePersonalDatabase(Legacy); Directory.CreateDirectory(Current);
        if (hasDatabase) File.WriteAllText(NewDatabase,"unrelated existing data");
        var original = File.ReadAllBytes(OldDatabase(Legacy));
        Assert.Throws<InvalidOperationException>(() => DataLocationMigration.Migrate(Legacy,Current));
        Assert.Equal(original,File.ReadAllBytes(OldDatabase(Legacy)));
        if (hasDatabase) Assert.Equal("unrelated existing data",File.ReadAllText(NewDatabase));
    }

    [Fact]
    public void BothDatabaseNamesArePreservedAndRejected()
    {
        CreatePersonalDatabase(Current); File.WriteAllText(NewDatabase,"existing database");
        Assert.Throws<InvalidOperationException>(() => DataLocationMigration.Migrate(Legacy,Current));
        Assert.True(File.Exists(OldDatabase(Current))); Assert.Equal("existing database",File.ReadAllText(NewDatabase));
    }

    [Fact]
    public void InterruptedFolderMoveResumesDatabaseRename()
    {
        CreatePersonalDatabase(Current);
        var before = File.ReadAllBytes(OldDatabase(Current));
        DataLocationMigration.Migrate(Legacy,Current);
        Assert.Equal(before,File.ReadAllBytes(NewDatabase));
        Assert.False(File.Exists(OldDatabase(Current)));
    }

    [Fact]
    public void IsolatedFormerFilenameMigratesWithoutLookingAtDefaultProfile()
    {
        CreatePersonalDatabase(Current);
        DataLocationMigration.Migrate(Current,Current);
        Assert.True(File.Exists(NewDatabase)); Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void InvalidDatabaseFailsBeforeMovingSourceAndCanRetry()
    {
        Directory.CreateDirectory(Legacy); File.WriteAllText(OldDatabase(Legacy),"invalid SQLite");
        Assert.Throws<SqliteException>(() => DataLocationMigration.Migrate(Legacy,Current));
        Assert.False(Directory.Exists(Current)); Assert.Equal("invalid SQLite",File.ReadAllText(OldDatabase(Legacy)));
        // Repair only this disposable synthetic fixture, then retry.
        File.Delete(OldDatabase(Legacy)); CreatePersonalDatabase(Legacy);
        DataLocationMigration.Migrate(Legacy,Current); Assert.True(File.Exists(NewDatabase));
    }

    [Fact]
    public void OpenDatabaseLockStopsMigrationBeforeAnyMove()
    {
        CreatePersonalDatabase(Legacy);
        using (var held = new FileStream(OldDatabase(Legacy),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            Assert.Throws<IOException>(() => DataLocationMigration.Migrate(Legacy,Current));
            Assert.True(File.Exists(OldDatabase(Legacy))); Assert.False(Directory.Exists(Current));
        }
        DataLocationMigration.Migrate(Legacy,Current); Assert.True(File.Exists(NewDatabase));
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("sidecars-first")]
    [InlineData("base-first")]
    public void CommittedWalAndInterruptedNameStatesArePreserved(string state)
    {
        var sourcePath=Path.Combine(root,"wal-source.db"); Directory.CreateDirectory(Current);
        var stagedBase = state=="base-first" ? NewDatabase : OldDatabase(Current);
        var stagedSidecars = state=="sidecars-first" ? NewDatabase : OldDatabase(Current);
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=sourcePath,Pooling=false }.ToString()))
        {
            writer.Open(); using var command=writer.CreateCommand();
            command.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE Evidence(Value TEXT); INSERT INTO Evidence VALUES ('committed WAL')";
            command.ExecuteNonQuery();
            File.Copy(sourcePath,stagedBase);
            foreach(var suffix in new[]{"-wal","-shm"}) File.Copy(sourcePath+suffix,stagedSidecars+suffix);
        }
        DataLocationMigration.Migrate(Legacy,Current);
        Assert.False(File.Exists(OldDatabase(Current)));
        Assert.False(File.Exists(OldDatabase(Current)+"-wal"));
        using var copied=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=NewDatabase,Pooling=false}.ToString());
        copied.Open(); using var read=copied.CreateCommand(); read.CommandText="SELECT Value FROM Evidence";
        Assert.Equal("committed WAL",read.ExecuteScalar());
    }

    [Theory]
    [InlineData("nerve_earnings_tracker.db")]
    [InlineData("EarningsTracker.db")]
    public void OrphanOrConflictingSidecarsStopBeforeCreatingBlankData(string filename)
    {
        Directory.CreateDirectory(Current); var orphan = Path.Combine(Current, filename) + "-wal"; File.WriteAllText(orphan,"preserved WAL");
        Assert.Throws<InvalidOperationException>(()=>DataLocationMigration.Migrate(Legacy,Current));
        Assert.False(File.Exists(NewDatabase));
        File.Delete(orphan); CreatePersonalDatabase(Current);
        File.WriteAllText(OldDatabase(Current)+"-wal","old WAL"); File.WriteAllText(NewDatabase+"-wal","new WAL");
        Assert.Throws<InvalidOperationException>(()=>DataLocationMigration.Migrate(Legacy,Current));
        Assert.Equal("old WAL",File.ReadAllText(OldDatabase(Current)+"-wal"));
        Assert.Equal("new WAL",File.ReadAllText(NewDatabase+"-wal"));
    }

    [Fact]
    public void FreshProfilesRemainUncreatedUntilOrdinaryInitialization()
    {
        DataLocationMigration.Migrate(Legacy,Current);
        Assert.False(Directory.Exists(Legacy)); Assert.False(Directory.Exists(Current));
    }

    [Fact]
    public void RelativeAndNestedPathsAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>DataLocationMigration.Migrate("relative",Current));
        Assert.Throws<InvalidOperationException>(()=>DataLocationMigration.Migrate(Legacy,Path.Combine(Legacy,"Nested")));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void RemoteTestRejectsBothProductionLocationsAndTheirBackups()
    {
        foreach(var path in new[]{Legacy,Current,Path.Combine(Legacy,"Backups"),Path.Combine(Current,"Backups")})
            Assert.Throws<InvalidOperationException>(()=>ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest",path,Legacy,Current));
        ApplicationUpdateConfiguration.ValidateDataDirectory("RemoteTest",root,Legacy,Current);
    }
}
