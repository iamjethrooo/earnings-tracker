using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using System.Data;
using System.IO;

namespace EarningsTracker.Utilities;

internal static class DatabaseInitialization
{
    internal static void Initialize(AppDbContext db)
    {
        var available = db.Database.GetMigrations().ToArray();
        var applied = db.Database.GetAppliedMigrations().ToArray();

        if (applied.Any(id => !available.Contains(id)) || !applied.SequenceEqual(available.Take(applied.Length)))
            throw new InvalidOperationException("This database uses an older development or newer application migration history. Use its matching application or the reviewed recovery procedure. The database has not been reset.");

        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == ConnectionState.Open;
        try
        {
            if (!wasOpen) connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'";
            if (applied.Length == 0 && Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InvalidOperationException("This database contains existing tables without the supported migration baseline. It has been preserved; review it before starting the tracker.");

            // SQLite's EF migration lock survives an interrupted process. Synchronous Migrate
            // otherwise retries indefinitely, leaving startup at the progress window forever.
            // Never clear it automatically: it may belong to another migration tool.
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name='__EFMigrationsLock'";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            {
                command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsLock WHERE Id=1";
                if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                    throw new InvalidOperationException("A database migration lock is still present. Close all tracker instances and migration tools. If an upgrade was interrupted, preserve the complete data folder and request reviewed recovery before clearing the lock. The database has not been reset.");
            }
        }
        finally { if (!wasOpen) connection.Close(); }

        if (applied.Length > 0 && db.Database.GetPendingMigrations().Any())
        {
            var path = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;
            if (!string.IsNullOrEmpty(path) && path != ":memory:")
            {
                var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "Backups");
                Directory.CreateDirectory(directory);
                var backup = Path.Combine(directory, $"before-migration-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db");
                CopyVerified(path, backup);
            }
        }
        db.Database.Migrate();
        DbInitializer.Initialize(db);
        AppSettings.InitializeDefaults(db);
    }

    internal static void CopyLegacyIfMissing(string destination, string legacy)
    {
        if (File.Exists(destination) || !File.Exists(legacy)) return;
        var temporary = destination + ".copy-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            CopyVerified(legacy, temporary);
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { /* Preserve a concurrently created destination. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void CopyVerified(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = destinationPath, Pooling = false }.ToString());
        source.Open(); destination.Open(); source.BackupDatabase(destination);
        using var integrity = destination.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
        using (var reader = integrity.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidDataException("Database backup integrity verification failed. The source database is preserved.");
        }
        using var foreignKeys = destination.CreateCommand(); foreignKeys.CommandText = "PRAGMA foreign_key_check";
        using var violations = foreignKeys.ExecuteReader();
        if (violations.Read())
            throw new InvalidDataException("Database backup contains foreign-key violations. Review the preserved source database before migration.");
    }
}
