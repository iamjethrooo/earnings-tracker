using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class SessionEarningsMatchingTests
    {
        private static List<Game> Games(decimal secondRate = 3m, decimal cap = 3000m) =>
        [
            new() { Id = 1, Name = "Example", RatePerHour = 5m, MinutesCap = cap },
            new() { Id = 2, Name = "Example", RatePerHour = secondRate, MinutesCap = cap }
        ];

        private static Session Parse(List<Game> games, string duration = "1h 0m", string amount = "+$5.00",
            string status = "Verified", int day = 5, Game? selected = null) => Assert.Single(SessionTextParser.Parse(
                $"Example\nExample · Jun {day}, 2026 12:00 PM · {duration} · File deleted\n{amount}\n{status}", games, selected));

        [Theory]
        [InlineData("1h 0m", "+$5.00", 3, 3000, 1)]
        [InlineData("1h 0m", "+$5.07", 3, 3000, 1)] // Minute truncation plus cent rounding.
        [InlineData("1h 0m", "+$3.00", 3, 3000, 2)] // Cap crossing still pays the full recording.
        [InlineData("1h 0m", "+$3.00", 3, 1, 2)] // Recording extends beyond the entire cap.
        [InlineData("1h 0m", "+$3.00", 3, 0, 2)]
        [InlineData("1h 0m", "+$0.00", 3, 3000, null)]
        [InlineData("1h 0m", "+$4.00", 3, 0, null)] // Historical rate/adjustment: no agreement.
        [InlineData("1h 0m", "+$6.00", 3, 3000, null)]
        [InlineData("1h 0m", "+$5.00", 5, 0, null)]
        [InlineData("1m", "+$0.06", 3, 3000, null)] // Both rates fit the duration precision.
        [InlineData("<1m", "+$0.01", 3, 0, null)]
        [InlineData("1h 0m", "+$5.00", 5.05, 0, null)]
        [InlineData("1h 0m 0s", "+$5.00", 5.05, 0, 1)]
        [InlineData("1h 0m 0s", "+$5.0001", 3, 0, null)] // Do not round precise earnings to cents.
        [InlineData("1h 0m", "+$5.00", 0, 0, null)] // Unknown rate cannot eliminate a game.
        [Trait("Category", "Regression")]
        public void OnlyUniqueEvidenceCanResolveDuplicateNames(string duration, string amount,
            double secondRate, int cap, int? expectedGame)
        {
            var session = Parse(Games((decimal)secondRate, cap), duration, amount);
            Assert.Equal(expectedGame, session.GameId);
            Assert.Equal(SessionStatus.Verified, session.Status);
            Assert.Equal("Example", session.GameName);
            Assert.Equal(SessionTextParser.ParseDuration(duration), session.Duration);
            Assert.Contains(amount, session.ImportSourceText!);
        }

        [Theory]
        [InlineData("File missing")]
        [InlineData("Rejected")]
        [InlineData("Uploading")]
        [InlineData("Verified (force-verified)")]
        [Trait("Category", "Regression")]
        public void UnverifiedOrForceVerifiedAmountsDoNotInferARate(string status)
        {
            Assert.Null(Parse(Games(), status: status).GameId);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MissingEarningsCanImportUnlinkedWithoutInventingAnAmount()
        {
            var session = Parse(Games(), amount: "", status: "File missing");
            Assert.Null(session.GameId);
            Assert.Equal(0m, session.Earnings);
            Assert.Equal(SessionStatus.NotUploaded, session.Status);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ExplicitGameDetailsSelectionKeepsExactDurationAndReportedEarnings()
        {
            var games = Games();
            var session = Assert.Single(SessionTextParser.Parse(
                "Nerve\nrecording-20260605120000000.mkv\n1h 2m 3s\n+$5.00\nVerified", games, games[1]));
            Assert.Equal(2, session.GameId);
            Assert.Equal(TimeSpan.FromSeconds(3723), session.Duration);
            Assert.Equal(5m, session.Earnings);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void EarningsMatchLinksAPreviouslyUnresolvedRecordingWithoutDuplicatingIt()
        {
            using var database = new TestDatabase();
            var games = Games();
            using (var seed = database.CreateContext()) { seed.Games.AddRange(games); seed.SaveChanges(); }
            var previous = Parse(games, amount: "+$3.00");
            previous.GameId = null; // Saved by the earlier conservative cap rule.
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([previous]));
            var preview = importer.PreviewImport([Parse(games, amount: "+$3.00")]);
            Assert.Empty(preview.NewSessions);
            Assert.Single(preview.Updates);
            importer.ImportSessions(preview);
            importer.ImportSessions(importer.PreviewImport([Parse(games, amount: "+$3.00")]));
            using var read = database.CreateContext();
            var saved = Assert.Single(read.Sessions);
            Assert.Equal(2, saved.GameId);
            Assert.Equal(3m, saved.Earnings);
            Assert.Equal(SessionStatus.Verified, saved.Status);
            Assert.Equal(2, read.SessionChanges.Count());
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MixedBatchImportsUnlinkedSessionsAndPreservesManualResolutionOnReimport()
        {
            using var database = new TestDatabase();
            var games = Games(secondRate: 5m);
            using (var seed = database.CreateContext()) { seed.Games.AddRange(games); seed.SaveChanges(); }
            List<Session> Batch() => [Parse(games, selected: games[0]), Parse(games, amount: "+$3.00", day: 6),
                Parse(games, amount: "", status: "File missing", day: 7)];
            var importer = new SessionImporter(database.Options);
            var parsed = Batch();
            var preview = importer.PreviewImport(parsed);
            Assert.Equal(3, preview.InsertedCount);
            Assert.Equal(2, preview.UnlinkedCount);
            Assert.Equal(0, preview.AmbiguousCount); // Record conflicts remain separate and skipped.
            importer.ImportSessions(preview);
            importer.ImportSessions(importer.PreviewImport(Batch()));
            using (var read = database.CreateContext())
            {
                Assert.Equal(3, read.Sessions.Count());
                Assert.Equal(3, read.SessionChanges.Count());
                Assert.Equal(8m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
                Assert.Equal(5m, read.Sessions.Where(s => s.GameId == 1).Sum(s => s.Earnings));
                Assert.Equal(0m, read.Sessions.Where(s => s.GameId == 2).Sum(s => s.Earnings));
            }
            var unresolved = parsed[1];
            unresolved.GameId = 2;
            SessionService.Update(unresolved.Id, unresolved, database.Options);
            // A later catalog rate change must not undo the explicit resolution on re-import.
            games[0].RatePerHour = 3m;
            games[1].RatePerHour = 5m;
            games.ForEach(game => game.MinutesCap = 0m);
            importer.ImportSessions(importer.PreviewImport(Batch()));
            using var final = database.CreateContext();
            Assert.Equal(3, final.Sessions.Count());
            Assert.Equal(1, final.Sessions.Count(s => s.GameId == 2));
            Assert.Equal(1, final.Sessions.Count(s => s.GameId == 1));
            Assert.Equal(1, final.Sessions.Count(s => s.GameId == null));
            Assert.Equal(4, final.SessionChanges.Count());
            Assert.Equal(3m, final.Sessions.Where(s => s.GameId == 2).Sum(s => s.Earnings));
            Assert.Equal(3m, final.Sessions.Single(s => s.RecordedDate.Day == 6).Earnings);
        }
    }
}
