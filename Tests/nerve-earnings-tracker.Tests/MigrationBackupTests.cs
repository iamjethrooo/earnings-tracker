using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Migrations;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using System.IO;
using System.Reflection;

namespace nerve_earnings_tracker.Tests;

public class MigrationBackupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingMigrationRequiresVerifiedBackupAndPreservesRecordsOnFailure(bool blockBackup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nerve-migration-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic.db");
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        try
        {
            using (var seed = new AppDbContext(options))
            {
                seed.Database.Migrate(); seed.Games.Add(new Game { Name = "Preserved synthetic game", RatePerHour = 1.2345m });
                seed.SaveChanges();
            }
            if (blockBackup) File.WriteAllText(Path.Combine(directory, "Backups"), "synthetic path obstruction");
            var pending = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection)
                .ReplaceService<IMigrationsAssembly, PendingMigrationAssembly>().Options;
            using (var db = new AppDbContext(pending))
            {
                if (blockBackup) Assert.Throws<IOException>(() => DatabaseInitialization.Initialize(db));
                else Assert.Contains("synthetic migration failure", Assert.Throws<InvalidOperationException>(() => DatabaseInitialization.Initialize(db)).Message);
            }
            using var read = new AppDbContext(options);
            Assert.Equal("Preserved synthetic game", Assert.Single(read.Games).Name);
            Assert.Equal(1.2345m, read.Games.Single().RatePerHour);
            Assert.Single(read.Database.GetAppliedMigrations());
            if (!blockBackup)
            {
                var backup = Assert.Single(Directory.GetFiles(Path.Combine(directory, "Backups"), "*.db"));
                using var restored = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()).Options);
                Assert.Equal("Preserved synthetic game", Assert.Single(restored.Games).Name);
                Assert.Equal(1.2345m, restored.Games.Single().RatePerHour);
                Assert.Single(restored.Database.GetAppliedMigrations());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Test-only future migration, using the real baseline model/snapshot. Nothing enters the app assembly.
    public sealed class PendingMigrationAssembly : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>
        {
            ["20261013055107_InitialCreate"] = typeof(InitialCreate).GetTypeInfo(),
            ["20261013100000_BackupProbe"] = typeof(FailingMigration).GetTypeInfo()
        };
        public ModelSnapshot? ModelSnapshot { get; } = new AppDbContextModelSnapshot();
        public Assembly Assembly => typeof(AppDbContext).Assembly;
        public string? FindMigrationId(string nameOrId) => Migrations.Keys.FirstOrDefault(id =>
            id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || id.EndsWith("_" + nameOrId, StringComparison.OrdinalIgnoreCase));
        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
        {
            var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
            migration.ActiveProvider = activeProvider; return migration;
        }
    }

    [Migration("20261013100000_BackupProbe")]
    public sealed class FailingMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => throw new InvalidOperationException("synthetic migration failure");
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException();
    }
}
