using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class SessionSameMinuteRecordingTests
    {
        private static List<Game> Games() =>
        [
            new() { Id = 1, Name = "Assassin's Creed Odyssey", RatePerHour = 1.5m },
            new() { Id = 2, Name = "Slay the Spire", RatePerHour = 1.5m }
        ];

        private static List<Session> Parse(List<Game> games) => SessionTextParser.Parse("""
            Assassin's Creed Odyssey
            Assassin's Creed Odyssey
            Assassin's Creed Odyssey · Sep 28, 2026 7:00 PM · 1h 5m · File deleted
            +$1.65
            Verified
            Assassin's Creed Odyssey
            Assassin's Creed Odyssey
            Assassin's Creed Odyssey · Sep 28, 2026 7:00 PM · <1m · File deleted
            Below minimum time
            Local file deleted
            Slay the Spire
            Slay the Spire
            Slay the Spire · Sep 5, 2026 4:11 PM · 38m · File deleted
            +$0.96
            Verified
            Slay the Spire
            Slay the Spire
            Slay the Spire · Sep 5, 2026 4:11 PM · <1m · File deleted
            Below minimum time
            Local file deleted
            """, games, null);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Category", "Regression")]
        public void CopiedPairsImportAndReimportWithoutSkippingOrCombiningRecordings(bool reverse)
        {
            using var database = new TestDatabase();
            var games = Games();
            using (var seed = database.CreateContext()) { seed.Games.AddRange(games); seed.SaveChanges(); }
            var importer = new SessionImporter(database.Options);
            var rows = Parse(games);
            if (reverse) rows.Reverse();
            var preview = importer.PreviewImport(rows);
            Assert.Equal(4, preview.InsertedCount);
            Assert.Equal(0, preview.AmbiguousCount);
            importer.ImportSessions(preview);
            importer.ImportSessions(importer.PreviewImport(Parse(games)));
            // Importing only one member later must target its own row.
            var correction = Parse(games).First();
            correction.Earnings = 1.66m;
            var update = importer.PreviewImport([correction]);
            Assert.Empty(update.NewSessions);
            Assert.Single(update.Updates);
            importer.ImportSessions(update);
            using var read = database.CreateContext();
            Assert.Equal(4, read.Sessions.Count());
            Assert.Equal(2, read.Sessions.Count(s => s.Status == SessionStatus.Verified));
            Assert.Equal(2, read.Sessions.Count(s => s.Status == SessionStatus.NotUploaded && s.Duration == TimeSpan.Zero && s.Earnings == 0m));
            Assert.Equal(5, read.SessionChanges.Count());
            Assert.Equal(2.62m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
            Assert.All(read.SessionChanges.Where(c => c.ChangeType == "Imported"), c => Assert.Contains("File deleted", c.SourceText!));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Category", "Regression")]
        public void SeparatelyPastedPairMembersDoNotOverwriteEachOther(bool shortFirst)
        {
            using var database = new TestDatabase();
            var games = Games();
            using (var seed = database.CreateContext()) { seed.Games.AddRange(games); seed.SaveChanges(); }
            var importer = new SessionImporter(database.Options);
            var rows = Parse(games).Take(2).ToList();
            if (shortFirst) rows.Reverse();
            importer.ImportSessions(importer.PreviewImport([rows[0]]));
            var second = importer.PreviewImport([rows[1]]);
            Assert.Single(second.NewSessions);
            Assert.Empty(second.Updates);
            importer.ImportSessions(second);
            importer.ImportSessions(importer.PreviewImport(Parse(games).Take(2)));
            using var read = database.CreateContext();
            Assert.Equal(2, read.Sessions.Count());
            Assert.Equal(2, read.SessionChanges.Count());
            Assert.Equal(1.65m, read.Sessions.Sum(s => s.Earnings));
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void PreciseTimestampCollisionStillRequiresResolution()
        {
            using var database = new TestDatabase();
            var rows = Parse(Games()).Take(2).ToList();
            foreach (var row in rows) { row.RecordedDate = row.RecordedDate.AddSeconds(1); row.Date = row.RecordedDate; }
            var preview = new SessionImporter(database.Options).PreviewImport(rows);
            Assert.Empty(preview.NewSessions);
            Assert.Equal(2, preview.AmbiguousCount);
        }
    }
}
