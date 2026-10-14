using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.IO;

namespace EarningsTracker.Data;

// EF tooling must never initialize or relocate the normal user's database.
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        if (args.Length != 2 || args[0] != "--database" || !Path.IsPathFullyQualified(args[1]))
            throw new ArgumentException("Specify an absolute scratch SQLite path: -- --database C:\\path\\design-time.db");

        var path = Path.GetFullPath(args[1]);
        if (Path.GetFileName(path).Equals(Utilities.AppDataPaths.LegacyDatabaseFileName, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals(Utilities.AppDataPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("EF tooling cannot target the normal tracker database. Use a scratch database path.");

        var connection = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }
}
