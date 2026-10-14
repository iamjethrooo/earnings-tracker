using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using EarningsTracker.Models;
using System.IO;

namespace EarningsTracker.Data
{
    class AppDbContext : DbContext
    {
        private IDisposable? updateWork;
        private void TrackUpdateWork() => updateWork ??= Utilities.UpdateRestartGuard.Shared.Enter();

        public override void Dispose()
        {
            try { base.Dispose(); }
            finally { Interlocked.Exchange(ref updateWork, null)?.Dispose(); }
        }

        public override async ValueTask DisposeAsync()
        {
            try { await base.DisposeAsync(); }
            finally { Interlocked.Exchange(ref updateWork, null)?.Dispose(); }
        }
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
            TrackUpdateWork();
            // Explicit database options must never initialize or copy the user's database.
            if (optionsBuilder.IsConfigured)
                return;

            Utilities.AppDataPaths.EnsureDataLocation();
            var appData = Utilities.AppDataPaths.DirectoryPath;
            Directory.CreateDirectory(appData);
            var path = Path.Combine(appData, Utilities.AppDataPaths.DatabaseFileName);
            var legacyPath = Path.Combine(AppContext.BaseDirectory, Utilities.AppDataPaths.LegacyDatabaseFileName);
            // Verified temporary copy includes committed WAL data; no existing database is replaced.
            if (Utilities.AppDataPaths.UsesProductionStorage)
                Utilities.DatabaseInitialization.CopyLegacyIfMissing(path, legacyPath);
            optionsBuilder.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
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
