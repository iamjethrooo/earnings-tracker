using System.Globalization;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    [Trait("Category", "Regression")]
    public class SessionSourceDateTests
    {
        [Theory]
        [InlineData("en-US", true)]
        [InlineData("de-DE", true)]
        [InlineData("fr-FR", true)]
        [InlineData("en-US", false)]
        [InlineData("de-DE", false)]
        [InlineData("fr-FR", false)]
        public void NerveMonthDayDatesDoNotDependOnWindowsCulture(string culture, bool gameDetails)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var game = new Game { Id = 1, Name = "Example" };
                var input = gameDetails
                    ? "Nerve\n6/5/2026 · 1h\n$10.00\nVerified"
                    : "Example\nvideo.mkv · 6/5/2026 · 1h\n$10.00\nVerified";
                var session = Assert.Single(SessionTextParser.Parse(input, [game], gameDetails ? game : null));
                Assert.Equal(new DateTime(2026, 6, 5), session.RecordedDate);
                Assert.Equal(session.RecordedDate, session.Date);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void NumericDisplayDateAgreesWithPreciseFilenameAcrossCultures(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var game = new Game { Id = 1, Name = "Example" };
                var input = "Nerve\nrecording-20260605123456123.mkv\n6/5/2026 · 1h\n$10.00\nVerified";
                var session = Assert.Single(SessionTextParser.Parse(input, [game], game));
                Assert.Equal(new DateTime(2026, 6, 5, 12, 34, 56, 123), session.RecordedDate);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Fact]
        public void ReimportAfterWindowsCultureChangesDoesNotDuplicateOrRedateARecording()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                using var database = new TestDatabase();
                var game = new Game { Id = 1, Name = "Example" };
                using (var db = database.CreateContext())
                {
                    db.Games.Add(game);
                    db.SaveChanges();
                }
                var importer = new SessionImporter(database.Options);
                var input = "Nerve\n6/5/2026 · 1h\n$10.00\nVerified";
                foreach (var culture in new[] { "en-US", "de-DE", "fr-FR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    importer.ImportSessions(importer.PreviewImport(SessionTextParser.Parse(input, [game], game)));
                }
                using var read = database.CreateContext();
                var session = Assert.Single(read.Sessions);
                Assert.Equal(new DateTime(2026, 6, 5), session.RecordedDate);
                Assert.Equal(session.RecordedDate, session.PostedDate);
                Assert.Single(read.SessionChanges);
                Assert.Equal(10m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
