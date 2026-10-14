using System.Text.Json;
using EarningsTracker.Enums;
using EarningsTracker.Exceptions;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class SessionGameAssociationTests
    {
        private static List<Game> Games() =>
        [
            new Game { Id = 1, Name = "Example", RatePerHour = 10m },
            new Game { Id = 2, Name = "Example", RatePerHour = 5m }
        ];

        private static Session Recording(int? gameId, decimal earnings = 10m)
        {
            var date = new DateTime(2026, 6, 5, 12, 0, 1);
            return new Session { GameId = gameId, GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromHours(1), Earnings = earnings, Status = SessionStatus.Verified };
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Category", "Regression")]
        public void ExplicitlySelectedGameWinsOverInferredHourlyRate(bool gameDetails)
        {
            var games = Games();
            var input = gameDetails
                ? "Nerve\nrecording-20260605120001000.mkv\n1h\n$10.00\nVerified"
                : "Example\nvideo.mkv · 2026-06-05T12:00:01 · 1h\n$10.00\nVerified";
            var session = Assert.Single(SessionTextParser.Parse(input, games, games[1]));
            Assert.Equal(2, session.GameId);
        }

        [Theory]
        [InlineData("$0.00")]
        [Trait("Category", "Regression")]
        public void DuplicateNamesWithoutASelectionRequireUserResolution(string earnings)
        {
            var session = Assert.Single(SessionTextParser.Parse(
                $"Example\nvideo.mkv · 2026-06-05T12:00:01 · 1h\n{earnings}\nVerified", Games(), null));
            Assert.Null(session.GameId);
            Assert.Equal("Example", session.GameName);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void DifferentGameLinkForAnExistingRecordingRequiresResolution()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.AddRange(Games());
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(1)]));
            var disputed = importer.PreviewImport([Recording(2, 5m)]);
            Assert.Empty(disputed.NewSessions);
            Assert.Empty(disputed.Updates);
            Assert.Single(disputed.AmbiguousSessions);
            importer.ImportSessions(disputed);
            importer.ImportSessions(importer.PreviewImport([Recording(1, 11m)]));
            using var read = database.CreateContext();
            Assert.Equal(1, Assert.Single(read.Sessions).GameId);
            Assert.Equal(11m, Assert.Single(read.Sessions).Earnings);
            Assert.Equal(2, read.SessionChanges.Count());
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void SameNameAndTimestampWithDifferentGameIdsAreConflictingVersions()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.AddRange(Games());
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([Recording(1), Recording(2, 5m)]);
            Assert.Equal(0, preview.InsertedCount);
            Assert.Equal(2, preview.AmbiguousCount);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
            Assert.Equal(0m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void GameIdAllowsARecordingToBeMatchedAfterTheGameIsRenamed()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.Add(Games()[0]);
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(1)]));
            var renamed = Recording(1, 11m);
            renamed.GameName = "Renamed";
            var preview = importer.PreviewImport([renamed]);
            Assert.Empty(preview.NewSessions);
            Assert.Single(preview.Updates);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Equal("Renamed", Assert.Single(read.Sessions).GameName);
            Assert.Equal(2, read.SessionChanges.Count());
        }

        [Fact]
        public void AnExplicitGameCanLinkAUniqueHistoricalUnlinkedRecordingWithAuditHistory()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.AddRange(Games());
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(null)]));
            var preview = importer.PreviewImport([Recording(2)]);
            Assert.Single(preview.Updates);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Equal(2, Assert.Single(read.Sessions).GameId);
            var change = read.SessionChanges.Single(change => change.ChangeType == "Imported update");
            using var before = JsonDocument.Parse(change.BeforeValuesJson);
            using var after = JsonDocument.Parse(change.AfterValuesJson);
            Assert.Equal(JsonValueKind.Null, before.RootElement.GetProperty("GameId").ValueKind);
            Assert.Equal(2, after.RootElement.GetProperty("GameId").GetInt32());
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MissingIncomingGameIdCannotEraseAnExistingAssociation()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.Add(Games()[0]);
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(1)]));
            var preview = importer.PreviewImport([Recording(null, 12m)]);
            Assert.Empty(preview.Updates);
            Assert.Single(preview.AmbiguousSessions);
            importer.ImportSessions(preview);
            using var read = database.CreateContext();
            Assert.Equal(1, Assert.Single(read.Sessions).GameId);
            Assert.Single(read.SessionChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ConflictingGameLinksToOneHistoricalRecordingAreAllAmbiguous()
        {
            using var database = new TestDatabase();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([Recording(null)]));
            var preview = importer.PreviewImport([Recording(1), Recording(2)]);
            Assert.Empty(preview.Updates);
            Assert.Equal(2, preview.AmbiguousCount);
        }
    }
}
