using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using System.IO;
using System.Text.Json;

namespace nerve_earnings_tracker.Tests
{
    public sealed class StartupInitializationTests : IDisposable
    {
        private readonly string _seedFile = Path.Combine(Path.GetTempPath(), $"nerve-test-seed-{Guid.NewGuid():N}.json");

        [Fact]
        public void ExistingGamesDoNotRequireSeedFileAndRepeatedInitializationPreservesData()
        {
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Games.Add(new Game { Name = "Existing game", RatePerHour = 2m });
                db.Sessions.Add(new Session { GameName = "Historic recording", Earnings = 1.23m });
                db.SaveChanges();
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var db = database.CreateContext();
                db.Database.Migrate();
                DbInitializer.Initialize(db, _seedFile);
            }
            using var read = database.CreateContext();
            Assert.Equal("Existing game", Assert.Single(read.Games).Name);
            Assert.Equal(1.23m, Assert.Single(read.Sessions).Earnings);
        }

        [Fact]
        public void MissingSeedFileSurfacesErrorWithoutChangingHistory()
        {
            using var database = new TestDatabase();
            using var db = database.CreateContext();
            db.Sessions.Add(new Session { GameName = "Historic recording", Earnings = 1.23m });
            db.SaveChanges();
            Assert.Throws<FileNotFoundException>(() => DbInitializer.Initialize(db, _seedFile));
            using var read = database.CreateContext();
            Assert.Empty(read.Games);
            Assert.Equal(1.23m, Assert.Single(read.Sessions).Earnings);
        }

        [Fact]
        public void MalformedSeedDoesNotInsertGames()
        {
            File.WriteAllText(_seedFile, "[{ broken json");
            using var database = new TestDatabase();
            using var db = database.CreateContext();
            Assert.Throws<JsonException>(() => DbInitializer.Initialize(db, _seedFile));
            using var read = database.CreateContext();
            Assert.Empty(read.Games);
        }

        [Fact]
        public void FailedSeedSaveRollsBackEarlierInsertsAndCanBeRetried()
        {
            File.WriteAllText(_seedFile, "[{\"Name\":\"First\"},{\"Name\":\"Second\"}]");
            using var database = new TestDatabase();
            using (var db = database.CreateContext())
            {
                db.Database.ExecuteSqlRaw("CREATE TRIGGER RejectSecond BEFORE INSERT ON Games WHEN NEW.Name = 'Second' BEGIN SELECT RAISE(ABORT, 'test seed failure'); END;");
                Assert.Throws<DbUpdateException>(() => DbInitializer.Initialize(db, _seedFile));
            }
            using (var read = database.CreateContext())
            {
                Assert.Empty(read.Games);
                read.Database.ExecuteSqlRaw("DROP TRIGGER RejectSecond");
            }
            using (var retry = database.CreateContext())
                DbInitializer.Initialize(retry, _seedFile);
            using var check = database.CreateContext();
            Assert.Equal(new[] { "First", "Second" }, check.Games.OrderBy(game => game.Id).Select(game => game.Name).ToArray());
        }

        public void Dispose() => File.Delete(_seedFile);
    }
}
