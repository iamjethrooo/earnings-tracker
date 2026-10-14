using System.IO;
using System.Security.Cryptography;

namespace EarningsTracker.Utilities;

// Filesystem transition only: SQLite schemas, records, migration IDs and credentials are unchanged.
internal static class DataLocationMigration
{
    private static readonly string[] Sidecars = ["-wal", "-shm", "-journal"];

    internal static void Migrate(string legacyDirectory, string currentDirectory)
    {
        if (!Path.IsPathFullyQualified(legacyDirectory) || !Path.IsPathFullyQualified(currentDirectory))
            throw new ArgumentException("Data-location migration requires absolute paths.");
        var legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyDirectory));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        var sameDirectory = legacy.Equals(current, StringComparison.OrdinalIgnoreCase);
        if (!sameDirectory && (legacy.StartsWith(current + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || current.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Data-location migration directories cannot overlap.");
        RejectRedirect(legacy); RejectRedirect(current);
        var moveDirectory = !sameDirectory && Directory.Exists(legacy);
        if (moveDirectory && Directory.Exists(current))
            throw new InvalidOperationException("Both former and current data folders exist. Neither was overwritten. Review them before retrying the data-location migration.");
        var directory = moveDirectory ? legacy : current;
        if (!Directory.Exists(directory)) return;
        var oldDatabase = Path.Combine(directory, AppDataPaths.LegacyDatabaseFileName);
        var newDatabase = Path.Combine(directory, AppDataPaths.DatabaseFileName);
        if (File.Exists(oldDatabase) && File.Exists(newDatabase))
            throw new InvalidOperationException("Both database filenames exist. Neither was overwritten. Review them before retrying the data-location migration.");
        var renameDatabase = File.Exists(oldDatabase);
        var renameSidecars = Sidecars.Any(suffix => File.Exists(oldDatabase + suffix));
        var database = renameDatabase ? oldDatabase : File.Exists(newDatabase) ? newDatabase : null;
        if (database == null && Sidecars.Any(suffix => File.Exists(oldDatabase + suffix) || File.Exists(newDatabase + suffix)))
            throw new InvalidOperationException("SQLite sidecar files exist without their database. They were preserved; review them before retrying.");
        foreach (var suffix in Sidecars)
            if (File.Exists(oldDatabase + suffix) && File.Exists(newDatabase + suffix))
                throw new InvalidOperationException("Conflicting SQLite sidecar filenames exist. They were preserved; review them before retrying.");

        if (!moveDirectory && !renameDatabase && !renameSidecars) return;

        var locks = new List<(string Suffix, FileStream Stream)>();
        try
        {
            LockDatabaseFiles(directory, locks);
            if (locks.Count > 0) CreateRecoverySnapshot(directory, locks);
            if (moveDirectory)
            {
                var before = Fingerprint(locks);
                // Windows cannot rename a directory with open child handles. Startup's stable profile
                // mutex excludes tracker instances; another tool holding a file makes Directory.Move fail.
                DisposeLocks(locks);
                Directory.Move(legacy, current);
                LockDatabaseFiles(current, locks);
                // If an external tool wrote and closed during the handle transition, preserve its latest
                // complete state in another verified snapshot rather than relying on the earlier one.
                if (locks.Count > 0 && !before.SequenceEqual(Fingerprint(locks))) CreateRecoverySnapshot(current, locks);
            }
            oldDatabase = Path.Combine(current, AppDataPaths.LegacyDatabaseFileName);
            newDatabase = Path.Combine(current, AppDataPaths.DatabaseFileName);
            // Rename sidecars first, base last; either interrupted state is recognized on retry.
            foreach (var suffix in Sidecars)
                if (File.Exists(oldDatabase + suffix)) File.Move(oldDatabase + suffix, newDatabase + suffix);
            if (File.Exists(oldDatabase)) File.Move(oldDatabase, newDatabase);
        }
        catch (IOException ex)
        {
            throw new IOException("The data-location migration could not finish. Close tracker instances or database tools and check folder permissions, then retry. Existing data and recovery snapshots were preserved.", ex);
        }
        finally { DisposeLocks(locks); }
    }

    private static void LockDatabaseFiles(string directory, List<(string Suffix, FileStream Stream)> locks)
    {
        var oldDatabase = Path.Combine(directory, AppDataPaths.LegacyDatabaseFileName);
        var newDatabase = Path.Combine(directory, AppDataPaths.DatabaseFileName);
        if (File.Exists(oldDatabase) && File.Exists(newDatabase))
            throw new InvalidOperationException("Both database filenames exist. Neither was overwritten; review them before retrying.");
        var database = File.Exists(oldDatabase) ? oldDatabase : File.Exists(newDatabase) ? newDatabase : null;
        if (database == null)
        {
            if (Sidecars.Any(suffix => File.Exists(oldDatabase + suffix) || File.Exists(newDatabase + suffix)))
                throw new InvalidOperationException("SQLite sidecar files exist without their database; they were preserved.");
            return;
        }
        // Exclusive access prevents snapshotting an active database; delete sharing permits file renames.
        OpenLocked(database, "", locks);
        foreach (var suffix in Sidecars)
        {
            if (File.Exists(oldDatabase + suffix) && File.Exists(newDatabase + suffix))
                throw new InvalidOperationException("Conflicting SQLite sidecar filenames exist; they were preserved.");
            var sidecar = File.Exists(oldDatabase + suffix) ? oldDatabase + suffix : newDatabase + suffix;
            if (File.Exists(sidecar)) OpenLocked(sidecar, suffix, locks);
        }
    }

    private static void CreateRecoverySnapshot(string directory, List<(string Suffix, FileStream Stream)> locks)
    {
        RejectRedirect(Path.Combine(directory, "Backups"));
        RejectRedirect(Path.Combine(directory, "Backups", "BeforeDataLocationMigration"));
        var archive = Path.Combine(directory, "Backups", "BeforeDataLocationMigration",
            $"{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}");
        var original = Path.Combine(archive, "Original");
        Directory.CreateDirectory(original);
        var rawDatabase = Path.Combine(original, Path.GetFileName(locks[0].Stream.Name));
        foreach (var entry in locks)
        {
            using var copy = new FileStream(rawDatabase + entry.Suffix, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            entry.Stream.Position = 0;
            entry.Stream.CopyTo(copy);
            copy.Flush(flushToDisk: true);
        }
        // Verify a standalone SQLite snapshot from the frozen copy, including committed WAL records.
        DatabaseInitialization.CopyVerified(rawDatabase, Path.Combine(archive, "Snapshot.db"));
    }

    private static string[] Fingerprint(List<(string Suffix, FileStream Stream)> locks)
        => locks.Select(entry =>
        {
            entry.Stream.Position = 0;
            return entry.Suffix + ":" + Convert.ToHexString(SHA256.HashData(entry.Stream));
        }).ToArray();

    private static void DisposeLocks(List<(string Suffix, FileStream Stream)> locks)
    {
        foreach (var entry in locks) entry.Stream.Dispose();
        locks.Clear();
    }

    private static void OpenLocked(string path, string suffix, List<(string Suffix, FileStream Stream)> locks)
    {
        RejectRedirect(path);
        locks.Add((suffix, new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete)));
    }

    private static void RejectRedirect(string path)
    {
        if ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Data-location migration cannot follow linked folders or files. Existing data was preserved; review the path before retrying.");
    }
}
