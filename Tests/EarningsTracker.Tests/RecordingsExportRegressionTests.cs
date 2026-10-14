using EarningsTracker.Enums;
using EarningsTracker.Exceptions;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class RecordingsExportRegressionTests
    {
        [Fact]
        [Trait("Category", "Regression")]
        public void WholePageExportKeepsEveryRecordingAndUsesTheDisplayedGameName()
        {
            // Representative rows from the supplied Nerve v0.1.78 Ctrl+A export.
            var input = """
                NERVE
                ⌂
                Home
                ▶
                Tasks
                ◉
                Recordings
                $
                Payments
                ?
                Help
                Sign Out
                v0.1.78
                Recordings
                Your recording history (608)
                Open Folder
                Delete uploaded
                Recording folder:
                C:\Users\Example\Videos\Nerve
                Change
                FILE
                All (608)
                Has File (0)
                No File (608)
                STATUS
                All (608)
                Not uploaded (20)
                Verified (586)
                Rejected (2)
                PlateUp!
                PlateUp!
                PlateUp! · Oct 11, 2026 3:13 AM · 24m · File deleted
                +$0.61
                Verified
                PlateUp!
                PlateUp!
                PlateUp! · Oct 10, 2026 11:41 PM · 1h 59m · File deleted
                +$3.00
                Verified
                PlateUp!
                PlateUp!
                PlateUp! · Oct 10, 2026 11:34 PM · <1m · File deleted
                Below minimum time
                Local file deleted
                Diablo IV
                Diablo 4
                Diablo IV · Oct 9, 2026 10:07 AM · 1h 17m · File deleted
                +$1.94
                Verified
                Planet Crafter
                The Planet Crafter
                Planet Crafter · Oct 4, 2026 8:18 PM · 38m · File deleted
                +$0.97
                Verified
                The Witcher 3: Wild Hunt
                The Witcher 3
                The Witcher 3: Wild Hunt · Sep 25, 2026 11:41 AM · 14m · File deleted
                +$0.36
                Verified
                Death Stranding
                Death Stranding
                Death Stranding · May 17, 2026 4:46 PM · 54m · File deleted
                File missing
                Local file deleted
                Trailmakers
                Trailmakers
                Trailmakers · May 14, 2026 1:19 PM · 7m · File deleted
                File missing
                Local file deleted
                Help
                Sign Out
                """;
            var names = new[] { "PlateUp!", "Diablo 4", "The Planet Crafter", "The Witcher 3", "Death Stranding", "Trailmakers", "Diablo IV" };
            var games = names.Select((name, i) => new Game { Id = i + 1, Name = name }).ToList();
            var sessions = SessionTextParser.Parse(input, games, null);
            Assert.Equal(8, sessions.Count);
            Assert.Equal(TimeSpan.FromMinutes(24), sessions[0].Duration);
            Assert.Equal(0.61m, sessions[0].Earnings);
            Assert.Equal(TimeSpan.FromMinutes(119), sessions[1].Duration);
            Assert.Equal(TimeSpan.Zero, sessions[2].Duration);
            Assert.Equal(SessionStatus.NotUploaded, sessions[2].Status);
            Assert.Equal(2, sessions[3].GameId); // Recording title must not override Diablo 4.
            Assert.Equal(3, sessions[4].GameId);
            Assert.Equal(4, sessions[5].GameId);
            Assert.All(sessions.Take(2).Concat(sessions.Skip(3).Take(3)), s => Assert.Equal(SessionStatus.Verified, s.Status));
            Assert.All(sessions.Skip(6), s => Assert.Equal(SessionStatus.NotUploaded, s.Status));
            Assert.All(sessions, s => Assert.Contains("File deleted", s.ImportSourceText!));
        }

        [Theory]
        [InlineData("Below minimum time", "<1m", SessionStatus.NotUploaded)]
        [InlineData("File missing", "54m", SessionStatus.NotUploaded)]
        [InlineData("Rejected", "1h 2m", SessionStatus.Rejected)]
        [InlineData("Uploading", "24m", SessionStatus.Pending)]
        [InlineData("Verifying", "24m", SessionStatus.Pending)]
        [InlineData("In review", "24m", SessionStatus.Pending)]
        [Trait("Category", "Regression")]
        public void FileAvailabilityDoesNotReplaceRecordingStatus(string status, string duration, SessionStatus expected)
        {
            var game = new Game { Id = 1, Name = "PlateUp!" };
            var session = Assert.Single(SessionTextParser.Parse(
                $"PlateUp!\nPlateUp!\nPlateUp! · Oct 10, 2026 11:34 PM · {duration} · File deleted\n{status}", [game], null));
            Assert.Equal(expected, session.Status);
            Assert.Equal(0m, session.Earnings);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void BelowMinimumNoticeKeepsTheExplicitRejectionFromTheExport()
        {
            var game = new Game { Id = 1, Name = "House Flipper 2" };
            var session = Assert.Single(SessionTextParser.Parse(
                "House Flipper 2\nHouse Flipper 2\nHouse Flipper 2 · Jul 12, 2026 7:45 PM · <1m · File deleted\nBelow minimum time\nRejected", [game], null));
            Assert.Equal(SessionStatus.Rejected, session.Status);
            Assert.Equal(TimeSpan.Zero, session.Duration);
            Assert.Contains("Rejected", session.ImportSourceText!);
        }

        [Theory]
        [InlineData("-$0.61")]
        [InlineData("$-0.61")]
        [Trait("Category", "Regression")]
        public void NewHeaderFormatDoesNotTurnNegativeEarningsIntoPositiveEarnings(string amount)
        {
            var game = new Game { Id = 1, Name = "PlateUp!" };
            var error = Assert.Throws<ArgumentException>(() => SessionTextParser.Parse(
                $"PlateUp!\nPlateUp! · Oct 11, 2026 3:13 AM · 24m · File deleted\n{amount}\nVerified", [game], null));
            Assert.Contains("earnings adjustment", error.Message);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void RecordingAliasCannotBypassMissingOrAmbiguousDisplayedGame()
        {
            const string input = "Diablo IV\nDiablo 4\nDiablo IV · Oct 9, 2026 11:27 AM · 13m · File deleted\n+$0.33\nVerified";
            var alias = new Game { Id = 1, Name = "Diablo IV" };
            Assert.Throws<GameNotFoundException>(() => SessionTextParser.Parse(input, [alias], alias));
            var first = new Game { Id = 2, Name = "Diablo 4" };
            var second = new Game { Id = 3, Name = "Diablo 4" };
            Assert.Null(Assert.Single(SessionTextParser.Parse(input, [alias, first, second], null)).GameId);
            Assert.Equal(3, Assert.Single(SessionTextParser.Parse(input, [alias, first, second], second)).GameId);
        }
    }
}
