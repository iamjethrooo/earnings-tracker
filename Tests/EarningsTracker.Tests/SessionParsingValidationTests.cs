using EarningsTracker.Enums;
using EarningsTracker.Exceptions;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class SessionParsingValidationTests
    {
        private static readonly Game Example = new() { Id = 1, Name = "Example" };

        [Theory]
        [InlineData("1h 2m 3s", 3723)]
        [InlineData("1h2m3s", 3723)]
        [InlineData(" 62m ", 3720)]
        [InlineData("13s", 13)]
        [InlineData("0m", 0)]
        [InlineData("<1m", 0)]
        [InlineData("< 1 m", 0)]
        public void SupportedDurationsKeepTheirExistingMeaning(string input, int seconds)
        {
            Assert.Equal(TimeSpan.FromSeconds(seconds), SessionTextParser.ParseDuration(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("unknown")]
        [InlineData("-5m")]
        [InlineData("1.5m")]
        [InlineData("<99m")]
        [InlineData("1m garbage")]
        [InlineData("1m 2m")]
        [InlineData("999999999999999999999h")]
        [InlineData("2147483647h")]
        [Trait("Category", "Regression")]
        public void InvalidDurationsAreRejectedInsteadOfInventingAValue(string input)
        {
            var error = Assert.Throws<FormatException>(() => SessionTextParser.ParseDuration(input));
            Assert.Contains("duration", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Category", "Regression")]
        public void MissingStatusAtEndOfInputIsReported(bool gameDetails)
        {
            var header = gameDetails
                ? "Nerve\nrecording-20260605123456123.mkv\n1m"
                : "Example\nvideo.mkv · 2026-06-05T12:34:56 · 1m";
            var error = Assert.Throws<InvalidSessionFormatException>(() =>
                SessionTextParser.Parse(header + "\n$1.55\nUnknown status", [Example], gameDetails ? Example : null));
            Assert.Contains("status", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Line", error.Message);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Category", "Regression")]
        public void ANewRecordingCannotSilentlyReplaceAnIncompleteOne(bool gameDetails)
        {
            var input = gameDetails
                ? "Nerve\nrecording-20260605123456123.mkv\n1m\n$1.55\nrecording-20260606123456123.mkv\n2m\n$2.00\nVerified"
                : "Example\nfirst.mkv · 2026-06-05T12:34:56 · 1m\n$1.55\nExample\nsecond.mkv · 2026-06-06T12:34:56 · 2m\n$2.00\nVerified";
            var error = Assert.Throws<InvalidSessionFormatException>(() =>
                SessionTextParser.Parse(input, [Example], gameDetails ? Example : null));
            Assert.Contains("status", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Nerve\nrecording-20260605123456123.mkv", true)]
        [InlineData("Nerve\nrecording.mkv\n1m\n$1.55\nVerified", true)]
        [InlineData("video.mkv · 2026-06-05T12:34:56 · 1m\n$1.55\nVerified", false)]
        [InlineData("Example\nvideo.mkv · 2026-06-05T12:34:56\n$1.55\nVerified", false)]
        [InlineData("Nerve\n2026-06-05T12:34:56 ·\n$1.55\nVerified", true)]
        [Trait("Category", "Regression")]
        public void TruncatedRecordingHeadersHaveAnActionableError(string input, bool gameDetails)
        {
            var error = Assert.Throws<InvalidSessionFormatException>(() =>
                SessionTextParser.Parse(input, [Example], gameDetails ? Example : null));
            Assert.Contains("Line", error.Message);
        }

        [Theory]
        [InlineData("Verified")]
        [InlineData("Paid")]
        [InlineData("Verified (force-verified)")]
        [Trait("Category", "Regression")]
        public void VerifiedRecordingsRequireAnExplicitEarningsValue(string status)
        {
            var error = Assert.Throws<InvalidSessionFormatException>(() => SessionTextParser.Parse(
                $"Nerve\nrecording-20260605123456123.mkv\n1m\n{status}", [Example], Example));
            Assert.Contains("earnings", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Uploading", SessionStatus.Pending)]
        [InlineData("Verifying", SessionStatus.Pending)]
        [InlineData("In review", SessionStatus.Pending)]
        [InlineData("Rejected", SessionStatus.Rejected)]
        [InlineData("Local file deleted", SessionStatus.NotUploaded)]
        [InlineData("File missing", SessionStatus.NotUploaded)]
        public void UnearnedStatusesStillAllowMissingEarnings(string status, SessionStatus expected)
        {
            var session = Assert.Single(SessionTextParser.Parse(
                $"Nerve\nrecording-20260605123456123.mkv\n1m\n{status}", [Example], Example));
            Assert.Equal(expected, session.Status);
            Assert.Equal(0m, session.Earnings);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OrdinaryBlankLinesDoNotBreakRecordingMetadata(bool gameDetails)
        {
            var input = gameDetails
                ? "Nerve\nrecording-20260605123456123.mkv\n\n1m\n$1.55\nVerified"
                : "Example\n\nvideo.mkv · 2026-06-05T12:34:56 · 1m\n$1.55\nVerified";
            var session = Assert.Single(SessionTextParser.Parse(input, [Example], gameDetails ? Example : null));
            Assert.Equal(TimeSpan.FromMinutes(1), session.Duration);
            Assert.Equal("Example", session.GameName);
        }

        [Fact]
        public void FilenameAndDisplayMetadataDescribeOneRecordingWithThePreciseFilenameDate()
        {
            var input = "Nerve\nrecording-20260605123456123.mkv\n2026-06-05T12:34:00 · 1m\n$1.55\nVerified";
            var session = Assert.Single(SessionTextParser.Parse(input, [Example], Example));
            Assert.Equal(new DateTime(2026, 6, 5, 12, 34, 56, 123), session.RecordedDate);
            Assert.Equal(TimeSpan.FromMinutes(1), session.Duration);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MetadataWithStatusWordsIsNotTreatedAsVerification()
        {
            var session = Assert.Single(SessionTextParser.Parse(
                "Nerve\nVerified-recording-20260605123456123.mkv\n1m\n$1.55\nRejected", [Example], Example));
            Assert.Equal(SessionStatus.Rejected, session.Status);
            Assert.Equal(1.55m, session.Earnings);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void GameNameCannotSupplyTheMissingStatusOfThePreviousRecording()
        {
            var nextGame = new Game { Id = 2, Name = "Verified Game" };
            Assert.Throws<InvalidSessionFormatException>(() => SessionTextParser.Parse(
                "Example\nfirst.mkv · 2026-06-05T12:34:56 · 1m\n$1.55\nVerified Game\nsecond.mkv · 2026-06-06T12:34:56 · 2m\n$2.00\nVerified",
                [Example, nextGame], null));
        }

        [Fact]
        public void ConflictingFilenameAndDisplayDatesRequireCorrection()
        {
            var error = Assert.Throws<InvalidSessionFormatException>(() => SessionTextParser.Parse(
                "Nerve\nrecording-20260605123456123.mkv\n2026-06-06T12:34:00 · 1m\n$1.55\nVerified", [Example], Example));
            Assert.Contains("dates", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void IncompleteBatchDoesNotReachPersistence()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Games.Add(new Game { Id = 1, Name = "Example" });
                seed.SaveChanges();
            }
            var importer = new SessionImporter(database.Options);
            Assert.Throws<InvalidSessionFormatException>(() =>
            {
                var parsed = SessionTextParser.Parse(
                    "Nerve\nrecording-20260605123456123.mkv\n1m\n$1.55\nVerified\nrecording-20260606123456123.mkv\n2m\n$2.00",
                    [Example], Example);
                importer.ImportSessions(importer.PreviewImport(parsed));
            });
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
        }

        [Theory]
        [InlineData("duration")]
        [InlineData("recorded date")]
        [InlineData("date mismatch")]
        [InlineData("status")]
        [Trait("Category", "Regression")]
        public void PreviewRejectsInvalidSessionValuesEvenWhenParsingIsBypassed(string invalidField)
        {
            using var database = new TestDatabase();
            var date = new DateTime(2026, 6, 5, 12, 0, 1);
            var session = new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(1), Earnings = 1m, Status = SessionStatus.Verified };
            switch (invalidField)
            {
                case "duration": session.Duration = TimeSpan.FromMinutes(-1); break;
                case "recorded date": session.RecordedDate = default; break;
                case "date mismatch": session.Date = date.AddDays(1); break;
                case "status": session.Status = (SessionStatus)99; break;
            }
            var importer = new SessionImporter(database.Options);
            Assert.Throws<ArgumentException>(() => importer.PreviewImport([session]));
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void InvalidatedDurationCannotSaveRecordsOrAuditHistory()
        {
            using var database = new TestDatabase();
            var date = new DateTime(2026, 6, 5, 12, 0, 1);
            var session = new Session { GameName = "Example", Date = date, RecordedDate = date,
                Duration = TimeSpan.FromMinutes(1), Earnings = 1m, Status = SessionStatus.Verified };
            var importer = new SessionImporter(database.Options);
            var preview = importer.PreviewImport([session]);
            session.Duration = TimeSpan.FromMinutes(-1);
            Assert.Throws<ArgumentException>(() => importer.ImportSessions(preview));
            using var read = database.CreateContext();
            Assert.Empty(read.Sessions);
            Assert.Empty(read.SessionChanges);
        }
    }
}
