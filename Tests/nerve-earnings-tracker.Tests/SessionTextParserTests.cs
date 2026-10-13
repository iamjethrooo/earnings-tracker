using System.Globalization;
using System.Text.Json;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;

namespace nerve_earnings_tracker.Tests
{
    public class SessionTextParserTests
    {
        public static TheoryData<string, bool> ImportFormats => new()
        {
            { "en-US", true }, { "de-DE", true }, { "fr-FR", true },
            { "en-US", false }, { "de-DE", false }, { "fr-FR", false }
        };

        private static List<Session> Parse(string amount, bool gameDetails)
        {
            var game = new Game { Id = 1, Name = "Example" };
            var input = gameDetails
                ? $"Nerve\nrecording-20260605123456123.mkv\n1m 1s\n{amount}\nVerified"
                : $"Example\nvideo.mkv · 2026-06-05T12:34:56 · 1m 1s\n{amount}\nVerified";
            return SessionTextParser.Parse(input, [game], gameDetails ? game : null);
        }

        [Theory]
        [MemberData(nameof(ImportFormats))]
        [Trait("Category", "Regression")]
        public void UsdAmountsKeepTheirValueRegardlessOfWindowsNumberFormat(string culture, bool gameDetails)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                (string Text, decimal Expected)[] amounts =
                [
                    ("$1.55", 1.55m), ("+$1.55", 1.55m), ("$+1.55", 1.55m),
                    ("  $ 1,234.56  ", 1234.56m), ("Earnings: $1.55", 1.55m),
                    ("$0.00", 0m), ("$1.2345", 1.2345m), ("$0001.5500", 1.55m),
                    ("$79228162514264337593543950335", decimal.MaxValue),
                    ("$0.0000000000000000000000000001", 0.0000000000000000000000000001m)
                ];
                foreach (var amount in amounts)
                    Assert.Equal(amount.Expected, Assert.Single(Parse(amount.Text, gameDetails)).Earnings);
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Theory]
        [MemberData(nameof(ImportFormats))]
        public void ParsedUsdEarningsReachStoredRecordsTotalsAndAuditWithoutChangingValue(string culture, bool gameDetails)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                using var database = new TestDatabase();
                using (var seed = database.CreateContext())
                {
                    seed.Games.Add(new Game { Id = 1, Name = "Example" });
                    seed.SaveChanges();
                }
                var importer = new SessionImporter(database.Options);
                importer.ImportSessions(importer.PreviewImport(Parse("$1.55", gameDetails)));
                using var read = database.CreateContext();
                var session = Assert.Single(read.Sessions);
                Assert.Equal(1.55m, session.Earnings);
                Assert.Equal(session.RecordedDate, session.PostedDate);
                Assert.Equal(1.55m, PayoutAccounting.Calculate(read.Sessions.ToList(), []).TotalEarned);
                using var audit = JsonDocument.Parse(Assert.Single(read.SessionChanges).AfterValuesJson);
                Assert.Equal(1.55m, audit.RootElement.GetProperty("Earnings").GetDecimal());
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Theory]
        [MemberData(nameof(ImportFormats))]
        [Trait("Category", "Regression")]
        public void NegativeSessionAmountsAreRejectedInsteadOfLosingTheirSign(string culture, bool gameDetails)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var amount in new[] { "-$1.55", "$-1.55", "Earnings: - $1.55" })
                {
                    var error = Assert.Throws<ArgumentException>(() => Parse(amount, gameDetails));
                    Assert.Contains("earnings adjustment", error.Message);
                }
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Theory]
        [InlineData("$1,55")]
        [InlineData("$1.234,56")]
        [InlineData("$1,23.45")]
        [InlineData("$1.55 garbage")]
        [InlineData("$1.55 $2.00")]
        [InlineData("$NaN")]
        [InlineData("$79228162514264337593543950336")]
        [InlineData("$0.12345678901234567890123456789")]
        [InlineData("($1.55)")]
        [InlineData("+$+1.55")]
        [Trait("Category", "Regression")]
        public void MalformedOrUnrepresentableAmountsAreRejected(string amount)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                var error = Assert.Throws<FormatException>(() => Parse(amount, true));
                Assert.Contains("USD", error.Message);
                Assert.Contains("Example", error.Message);
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }
    }
}
