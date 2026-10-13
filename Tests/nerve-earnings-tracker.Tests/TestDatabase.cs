using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using nerve_earnings_tracker.Data;

namespace nerve_earnings_tracker.Tests
{
    // Each test owns one connection; closing it destroys all of its data.
    internal sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
        public DbContextOptions<AppDbContext> Options { get; }

        public TestDatabase(string? targetMigration = null)
        {
            _connection.Open();
            Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            using var db = CreateContext();
            if (targetMigration == null)
                db.Database.Migrate();
            else
                db.GetService<IMigrator>().Migrate(targetMigration);
        }

        public AppDbContext CreateContext() => new(Options);
        public void Dispose() => _connection.Dispose();
    }
}
