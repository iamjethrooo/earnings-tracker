using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using nerve_earnings_tracker.Models;
using System.IO;

namespace nerve_earnings_tracker.Data
{
    class AppDbContext : DbContext
    {
        public AppDbContext() { }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Game> Games { get; set; }
        public DbSet<GameMetadata> GameMetadata { get; set; }
        public DbSet<GameGenre> GameGenres { get; set; }
        public DbSet<StoreOffer> StoreOffers { get; set; }
        public DbSet<SubscriptionAvailability> SubscriptionAvailability { get; set; }
        public DbSet<GameOwnership> GameOwnership { get; set; }
        public DbSet<CatalogSetting> CatalogSettings { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<GameChange> GameChanges { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<SessionChange> SessionChanges { get; set; }
        public DbSet<Payout> Payouts { get; set; }
        public DbSet<PayoutChange> PayoutChanges { get; set; }
        public DbSet<Bookmark> Bookmarks { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Explicit database options must never initialize or copy the user's database.
            if (optionsBuilder.IsConfigured)
                return;

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NerveEarningsTracker");
            Directory.CreateDirectory(appData);
            var path = Path.Combine(appData, "nerve_earnings_tracker.db");
            var legacyPath = Path.Combine(AppContext.BaseDirectory, "nerve_earnings_tracker.db");
            if (!File.Exists(path) && File.Exists(legacyPath))
            {
                // SQLite backup includes committed WAL data and leaves the old database untouched.
                using var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = legacyPath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString());
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path
                }.ToString());
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }
            optionsBuilder.UseSqlite($"Data Source={path}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            CatalogModelConfiguration.Configure(modelBuilder);
            modelBuilder.Entity<Session>()
                .HasOne(s => s.Game)
                .WithMany()
                .HasForeignKey(s => s.GameId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Session>()
                .HasOne(s => s.Payout)
                .WithMany()
                .HasForeignKey(s => s.PayoutId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Bookmark>()
                .HasOne(b => b.Game)
                .WithOne()
                .HasForeignKey<Bookmark>(b => b.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayoutChange>()
                .HasOne(c => c.Payout)
                .WithMany(p => p.Changes)
                .HasForeignKey(c => c.PayoutId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Payout>()
                .HasIndex(p => p.SourceId);

            modelBuilder.Entity<SessionChange>()
                .HasIndex(change => change.SessionId);

            modelBuilder.Entity<GameChange>()
                .HasIndex(change => change.GameId);

            modelBuilder.Entity<Payout>()
                .HasQueryFilter(p => !p.IsDeleted);
        }
    }
}
