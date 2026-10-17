using System.IO;
using System.Security.Cryptography;

namespace EarningsTracker.Utilities;

// Copy-only transition out of Velopack-owned folders. Called under the existing production
// instance mutex, before opening EF. Publish the complete profile with a single directory rename.
internal static class UserDataMigration
{
    private static readonly string[] DatabaseNames = [AppDataPaths.DatabaseFileName, AppDataPaths.LegacyDatabaseFileName];
    private static readonly string[] Sidecars = ["-wal", "-shm", "-journal"];

    internal static bool Migrate(string destination, params string[] sources)
    {
        destination = Normalize(destination);
        sources = sources.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var source in sources)
            if (Overlaps(source, destination))
                throw new InvalidOperationException("User data must be outside the installation and former data folders.");
        AppDataPaths.RejectLinkedPath(destination);
        if (Directory.Exists(destination))
        {
            // A completed profile is authoritative. Never import an old copy again after reinstall.
            if (File.Exists(Path.Combine(destination, AppDataPaths.DatabaseFileName))) return false;
            throw new InvalidOperationException("The user data folder already exists without a database. It was preserved; review it before retrying. Do not uninstall the former installation yet.");
        }

        var candidates = sources.Where(HasData).ToArray();
        if (candidates.Length > 1)
            throw new InvalidOperationException("Multiple former data locations contain saved data. All were preserved; review them before retrying. Do not uninstall the former installation yet.");
        if (candidates.Length == 0) return false;
        var directory = candidates[0];
        AppDataPaths.RejectLinkedPath(directory);
        var databases = DatabaseNames.Select(name => Path.Combine(directory, name)).Where(File.Exists).ToArray();
        if (databases.Length != 1)
            throw new InvalidOperationException("The former data folder has missing or conflicting databases. It was preserved; review it before retrying. Do not uninstall it yet.");

        var files = new List<(string Source, string Relative)> { (databases[0], AppDataPaths.DatabaseFileName) };
        foreach (var suffix in Sidecars)
        {
            var matches = DatabaseNames.Select(name => Path.Combine(directory, name + suffix)).Where(File.Exists).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("Conflicting SQLite sidecars were preserved. Review them before retrying the user data transition.");
            if (matches.Length == 1) files.Add((matches[0], AppDataPaths.DatabaseFileName + suffix));
        }

        var locks = new List<(FileStream Stream, string Relative)>();
        try
        {
            // Freeze base and committed WAL together. An active old app/tool makes this fail closed.
            foreach (var file in files)
            {
                RejectLink(file.Source);
                locks.Add((new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.None), file.Relative));
            }
            var staging = destination + ".preparing-" + Guid.NewGuid().ToString("N");
            var recovery = Path.Combine(staging, "Backups", "BeforeInstallSeparation");
            var original = Path.Combine(recovery, "Original");
            Directory.CreateDirectory(original);
            foreach (var entry in locks) CopyAndVerify(entry.Stream, Path.Combine(original, entry.Relative));
            var snapshot = Path.Combine(recovery, "Snapshot.db");
            DatabaseInitialization.CopyVerified(Path.Combine(original, AppDataPaths.DatabaseFileName), snapshot);
            // A second verified SQLite backup becomes the working database. Schemas/history stay intact.
            DatabaseInitialization.CopyVerified(snapshot, Path.Combine(staging, AppDataPaths.DatabaseFileName));
            foreach (var name in new[] { "Backups", "Artwork" })
                if (Directory.Exists(Path.Combine(directory, name)))
                    CopyTree(Path.Combine(directory, name), Path.Combine(staging, name));
            File.WriteAllText(Path.Combine(staging, "storage-transition.txt"),
                "Copy completed from: " + directory + Environment.NewLine +
                "Use this profile with EarningsTracker.App. Former files were retained. Do not use the former app to edit them.");
            Directory.Move(staging, destination); // No replacement; failed/partial preparations remain recoverable.
            return true;
        }
        catch (IOException ex)
        {
            throw new IOException("User data could not be copied safely. Close other tracker instances/database tools, check disk space and permissions, then retry. Source files and recovery copies were retained. Do not uninstall the former installation yet.", ex);
        }
        finally { foreach (var entry in locks) entry.Stream.Dispose(); }
    }

    private static bool HasData(string path)
    {
        AppDataPaths.RejectLinkedPath(path);
        return DatabaseNames.Any(name => File.Exists(Path.Combine(path, name))
            || Sidecars.Any(suffix => File.Exists(Path.Combine(path, name + suffix))))
            || Directory.Exists(Path.Combine(path, "Backups")) || Directory.Exists(Path.Combine(path, "Artwork"));
    }

    private static void CopyTree(string source, string destination)
    {
        RejectLink(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            RejectLink(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            CopyAndVerify(stream, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var child in Directory.EnumerateDirectories(source))
            CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
    }

    private static void CopyAndVerify(FileStream source, string destination)
    {
        using (var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            source.Position = 0;
            source.CopyTo(copy);
            copy.Flush(flushToDisk: true);
        }
        source.Position = 0;
        var expected = SHA256.HashData(source);
        using var read = File.OpenRead(destination);
        if (!expected.SequenceEqual(SHA256.HashData(read)))
            throw new IOException("User data copy verification failed. The source was preserved.");
    }

    private static string Normalize(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new ArgumentException("User data transition requires absolute paths.");
    private static bool Overlaps(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("User data transition cannot follow linked files or directories. Existing data was preserved.");
    }
}
