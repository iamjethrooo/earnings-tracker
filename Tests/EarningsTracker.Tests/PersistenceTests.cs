using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using EarningsTracker.Models;
using EarningsTracker.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EarningsTracker.Tests
{
    public class PersistenceTests
    {
        [Fact]
        public void ExplicitOptionsUseOnlyTheIsolatedMemoryDatabase()
        {
            using var database = new TestDatabase();
            using var db = database.CreateContext();
            var connection = new SqliteConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
            Assert.Equal(":memory:", connection.DataSource);
            Assert.Empty(db.Games);
            Assert.Empty(db.Sessions);
            Assert.Empty(db.Payouts);
        }

        [Fact]
        public void FreshDatabaseAppliesAllMigrationsAndRepeatedMigrationPreservesData()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                Assert.Equal(seed.Database.GetMigrations(), seed.Database.GetAppliedMigrations());
                Assert.Equal(seed.Database.GetMigrations(), seed.Database.GetAppliedMigrations());
                Assert.False(seed.Database.HasPendingModelChanges());
                seed.Games.Add(new Game { Name = "Test game", RatePerHour = 1m, MinutesCap = 60m });
                seed.SaveChanges();
            }
            using var reopened = database.CreateContext();
            reopened.Database.Migrate();
            Assert.Equal("Test game", Assert.Single(reopened.Games).Name);
        }

        [Fact]
        public void SeparateTestDatabasesCannotSeeEachOthersData()
        {
            using var first = new TestDatabase();
            using var second = new TestDatabase();
            using (var write = first.CreateContext())
            {
                write.Games.Add(new Game { Name = "First database only" });
                write.SaveChanges();
            }
            using var read = second.CreateContext();
            Assert.Empty(read.Games);
        }

        [Fact]
        public void MultiplayerUpgradeRetainsLegacyMetadataAndFinancialHistoryAndCanRepeat()
        {
            using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            using var db = new AppDbContext(options);
            db.GetService<IMigrator>().Migrate("20261107145034_AddSessionPayoutAssignments");
            db.Games.Add(new Game { Name = "Legacy game", RatePerHour = 1.2345m, MinutesCap = 90.5m });
            db.SaveChanges();
            var game = db.Games.Single();
            db.Database.ExecuteSqlRaw("INSERT INTO GameMetadata (GameId,CatalogId,Summary,CoverUrl,CoverSha256,Provenance,Reviewed) VALUES ({0},'legacy-id','Original summary','','','Original provenance',1)", game.Id);
            db.Sessions.Add(new Session { GameId = game.Id, GameName = game.Name, Earnings = 1.2345m });
            db.SessionChanges.Add(new SessionChange { SessionId = 1, ChangeType = "Imported", SourceText = "Original evidence" });
            db.Bookmarks.Add(new Bookmark { GameId = game.Id });
            db.GameOwnership.Add(new GameOwnership { GameId = game.Id, IsOwned = true, PurchaseAmount = 42.50m });
            db.SaveChanges();
            db.Database.Migrate(); db.Database.Migrate();
            var metadata = db.GameMetadata.Single();
            Assert.Null(metadata.SupportsMultiplayer);
            Assert.Null(metadata.MultiplayerVerifiedAt);
            Assert.Equal("", metadata.MultiplayerProvenance);
            Assert.Equal("Original summary", metadata.Summary);
            Assert.Equal("Original provenance", metadata.Provenance);
            Assert.Equal(1.2345m, db.Sessions.Single().Earnings);
            Assert.Equal("Original evidence", db.SessionChanges.Single().SourceText);
            Assert.Equal(42.50m, db.GameOwnership.Single().PurchaseAmount);
            Assert.Single(db.Bookmarks);
            Assert.Equal(1.2345m, db.Games.Single().RatePerHour);
            Assert.Equal(90.5m, db.Games.Single().MinutesCap);
            Assert.False(db.Database.HasPendingModelChanges());
        }

        [Fact]
        public void RepeatedBaselineMigrationPreservesSessionsPayoutRangesAndBookmarks()
        {
            using var database = new TestDatabase();
            using (var legacy = database.CreateContext())
            {
                // Explicit current values replace the historical development-only backfills.
                legacy.Database.ExecuteSqlRaw("INSERT INTO Games (Id, Name, RatePerHour, MinutesCap, IsArchived) VALUES (1, 'Legacy game', '1.50', '60', 0);");
                legacy.Payouts.Add(new Payout { Id = 1, StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2026, 6, 30),
                    Date = new DateTime(2026, 6, 1), Type = "Payment", Status = "Legacy" });
                legacy.SaveChanges();
                legacy.Sessions.Add(new Session { GameId = 1, GameName = "Legacy game", PayoutId = 1,
                    Date = new DateTime(2026, 6, 5, 12, 0, 0), RecordedDate = new DateTime(2026, 6, 5, 12, 0, 0),
                    PostedDate = new DateTime(2026, 6, 5, 12, 0, 0), Duration = TimeSpan.FromMinutes(10),
                    Status = Enums.SessionStatus.Verified, Earnings = 1.25m });
                legacy.SaveChanges();
                legacy.Database.ExecuteSqlRaw("INSERT INTO Bookmarks (GameId) VALUES (1);");
                legacy.Database.Migrate();
            }
            using var read = database.CreateContext();
            Assert.Equal("Legacy game", Assert.Single(read.Games).Name);
            Assert.Single(read.Bookmarks);
            var session = Assert.Single(read.Sessions);
            Assert.Equal(1, session.GameId);
            Assert.Equal(1, session.PayoutId);
            Assert.Equal(1.25m, session.Earnings);
            Assert.Equal(TimeSpan.FromMinutes(10), session.Duration);
            Assert.Equal(new DateTime(2026, 6, 5, 12, 0, 0), session.RecordedDate);
            Assert.Equal(session.Date, session.PostedDate);
            var payment = Assert.Single(read.Payouts);
            Assert.Equal(new DateTime(2026, 6, 1), payment.StartDate);
            Assert.Equal(new DateTime(2026, 6, 30), payment.EndDate);
            Assert.Equal(payment.StartDate, payment.Date);
            Assert.Equal("Legacy", payment.Status);
            Assert.Empty(read.PayoutChanges);
            Assert.Empty(read.SessionChanges);
        }

        [Fact]
        public void RepeatedBaselineMigrationPreservesPostedAndUnknownForceVerificationDates()
        {
            using var database = new TestDatabase();
            using (var legacy = database.CreateContext())
            {
                legacy.Database.ExecuteSqlRaw("INSERT INTO Sessions (GameName, Date, RecordedDate, PostedDate, Duration, Status, Earnings, IsForceVerified) VALUES ('Ordinary', '2026-06-05 00:00:00', '2026-06-05 00:00:00', '2026-06-05 00:00:00', '00:10:00', 1, '1', 0);");
                legacy.Database.ExecuteSqlRaw("INSERT INTO Sessions (GameName, Date, RecordedDate, PostedDate, Duration, Status, Earnings, IsForceVerified) VALUES ('Force missing', '2026-06-05 00:00:00', '2026-06-05 00:00:00', NULL, '00:10:00', 1, '1', 1);");
                legacy.Database.ExecuteSqlRaw("INSERT INTO Sessions (GameName, Date, RecordedDate, PostedDate, Duration, Status, Earnings, IsForceVerified) VALUES ('Force known', '2026-06-05 00:00:00', '2026-06-05 00:00:00', '2026-06-10 00:00:00', '00:10:00', 1, '1', 1);");
                legacy.Database.Migrate();
            }
            using var read = database.CreateContext();
            Assert.Equal(new DateTime(2026, 6, 5), read.Sessions.Single(s => s.GameName == "Ordinary").PostedDate);
            Assert.Null(read.Sessions.Single(s => s.GameName == "Force missing").PostedDate);
            Assert.Equal(new DateTime(2026, 6, 10), read.Sessions.Single(s => s.GameName == "Force known").PostedDate);
        }
    }
}
