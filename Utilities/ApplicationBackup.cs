using System.IO;
using System.Security.Cryptography;
using EarningsTracker.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EarningsTracker.Utilities;

// A user backup is a standalone SQLite database. SQLite's backup API includes committed WAL
// transactions and keeps the source usable while the copy is made.
internal static class ApplicationBackup
{
    private const string PendingName = "PendingRestore";
    private const string StagedName = "Staged.db";
    private const string HashName = "SHA256.txt";
    private const string ApplyingName = "Applying.txt";

    internal static string Create(string dataDirectory, string destination)
    {
        var source = Path.Combine(dataDirectory, AppDataPaths.DatabaseFileName);
        if (!File.Exists(source)) throw new FileNotFoundException("The application database is unavailable.", source);
        var fullDestination = Path.GetFullPath(destination);
        if (File.Exists(fullDestination)) throw new IOException("The selected backup file already exists. Choose a new name.");
        var directory = Path.GetDirectoryName(fullDestination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".earnings-backup-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            DatabaseInitialization.CopyVerified(source, temporary);
            Validate(temporary);
            File.Move(temporary, fullDestination);
            return fullDestination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void Validate(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected backup is unavailable.", path);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check";
            using var result = command.ExecuteReader();
            if (!result.Read() || result.GetString(0) != "ok" || result.Read())
                throw new InvalidDataException("The selected database failed SQLite integrity validation.");
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_key_check";
            using var result = command.ExecuteReader();
            if (result.Read()) throw new InvalidDataException("The selected database has broken references.");
        }
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var available = db.Database.GetMigrations().ToArray();
        var applied = db.Database.GetAppliedMigrations().ToArray();
        if (applied.Length == 0 || !applied.SequenceEqual(available))
            throw new InvalidDataException("The selected backup does not use this application's current migration history.");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table'";
            using var result = command.ExecuteReader();
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (result.Read()) tables.Add(result.GetString(0));
            if (db.Model.GetEntityTypes().Select(entity => entity.GetTableName()).Where(name => name != null)
                .Any(name => !tables.Contains(name!)))
                throw new InvalidDataException("The selected backup is missing an application table.");
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsLock WHERE Id=1";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InvalidDataException("The selected backup contains an unfinished migration lock.");
        }
    }

    internal static void ScheduleRestore(string dataDirectory, string selectedBackup)
    {
        var backups = Path.Combine(dataDirectory, "Backups");
        var pending = Path.Combine(backups, PendingName);
        if (Directory.Exists(pending)) throw new InvalidOperationException("A restore is already pending. Restart the application to finish it.");
        Directory.CreateDirectory(backups);
        var preparing = Path.Combine(backups, ".restore-preparing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(preparing);
        try
        {
            var staged = Path.Combine(preparing, StagedName);
            DatabaseInitialization.CopyVerified(selectedBackup, staged);
            Validate(staged);
            File.WriteAllText(Path.Combine(preparing, HashName), Hash(staged));
            Directory.Move(preparing, pending);
        }
        finally { if (Directory.Exists(preparing)) Directory.Delete(preparing, recursive: true); }
    }

    // Called under the normal instance mutex, before any application DbContext is opened.
    // A failed or interrupted operation keeps the recovery copy and fails startup closed.
    internal static string? ApplyPendingRestore(string dataDirectory)
    {
        var pending = Path.Combine(dataDirectory, "Backups", PendingName);
        if (!Directory.Exists(pending)) return null;
        var staged = Path.Combine(pending, StagedName);
        var expectedHash = File.ReadAllText(Path.Combine(pending, HashName)).Trim();
        if (!string.Equals(Hash(staged), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The staged restore changed. Current data was preserved. Review the pending restore folder.");
        Validate(staged);
        var current = Path.Combine(dataDirectory, AppDataPaths.DatabaseFileName);
        if (!File.Exists(current)) throw new InvalidDataException("The current database is missing. Restore was not applied.");

        var recoveryDirectory = Path.Combine(dataDirectory, "Backups", "BeforeUserRestore");
        Directory.CreateDirectory(recoveryDirectory);
        var recovery = Path.Combine(recoveryDirectory, $"before-restore-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.db");
        DatabaseInitialization.CopyVerified(current, recovery);
        Validate(recovery);
        var currentHash = Hash(recovery);
        var applying = Path.Combine(pending, ApplyingName);
        if (File.Exists(applying))
        {
            var originalHash = File.ReadAllText(applying).Trim();
            if (string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(pending, recursive: true);
                return recovery;
            }
            if (!string.Equals(currentHash, originalHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The database changed during an interrupted restore. No further replacement was attempted; review the recovery backups.");
        }
        else File.WriteAllText(applying, currentHash);

        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = staged, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = current, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            source.Open(); destination.Open(); source.BackupDatabase(destination);
        }
        Validate(current);
        Directory.Delete(pending, recursive: true);
        return recovery;
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
