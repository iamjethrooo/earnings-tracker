using System.Globalization;
using System.Text.Json;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class PayoutTextParserTests
    {
        [Theory]
        [InlineData("**-$1,234.56**", "-1234.56")]
        [InlineData("+$0.01", "0.01")]
        [InlineData("$100.59", "100.59")]
        public void MarkdownPreservesSignedAmountsAndOriginalValues(string source, string expected)
        {
            var row = $"|6/5/2026|—|Payment|Batch payout|Paid|{source}|";
            var parsed = PayoutTextParser.ParseDetailed(row);
            var payout = Assert.Single(parsed.Payouts);
            Assert.Empty(parsed.FailedRows);
            Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), payout.Amount);
            Assert.Equal(row, payout.SourceRow);
            using var original = JsonDocument.Parse(payout.SourceValuesJson!);
            Assert.Equal(source.Replace("**", ""), original.RootElement.GetProperty("Amount").GetString());
        }

        [Fact]
        public void SupportsHeadersSeparatorsAndOrdinaryEarningsAreSkipped()
        {
            var parsed = PayoutTextParser.ParseDetailed("""
                | Posted | Recorded | Type | Description | Status | Amount | Transaction ID |
                | --- | --- | --- | --- | --- | ---: | --- |
                | 6/5/2026 | — | **Bonus** | Bug reports | Verified | +$20.00 | BONUS-1 |
                | 6/6/2026 | — | Earnings correction | Cap correction | Verified | -$0.01 | FIX-1 |
                | 6/5/2026 | 6/5/2026 | Earning | Example — 1 min recording | Verified | +$0.10 | — |
                """);
            Assert.Equal(2, parsed.Payouts.Count);
            Assert.Equal("BONUS-1", parsed.Payouts[0].SourceId);
            Assert.Equal(-0.01m, parsed.Payouts[1].Amount);
            Assert.Single(parsed.SkippedRows);
            Assert.Empty(parsed.ForceVerifiedSessions);
            Assert.Empty(parsed.FailedRows);
        }

        [Fact]
        public void SupportsSuppliedPlainTextPayments()
        {
            var parsed = PayoutTextParser.ParseDetailed("""
                6/3/2026 — Payment Payment for 11 session(s) via Tipalti Failed $23.84
                6/10/2026 — Payment Payment for 14 session(s) + 1 bonus(es) via Tipalti Paid -$52.41
                10/7/2026 — Payment Batch payout: 124 session(s) via Tipalti Processing $100.59
                """);
            Assert.Equal(3, parsed.Payouts.Count);
            Assert.Equal(new[] { 23.84m, -52.41m, 100.59m }, parsed.Payouts.Select(p => p.Amount));
            Assert.Empty(parsed.FailedRows);
        }

        [Theory]
        [InlineData("$1,23")]
        [InlineData("$1.20oops")]
        [InlineData("($5.00)")]
        [InlineData("$1.001")]
        [InlineData("$1.230000000000000000000000000001")]
        [InlineData("$7922816251426433759354395033.51")]
        [InlineData("$0.00")]
        [Trait("Category", "Regression")]
        public void MalformedMarkdownAmountsAreRejected(string amount)
        {
            var parsed = PayoutTextParser.ParseDetailed($"|6/5/2026|—|Payment|Batch payout|Paid|{amount}|");
            Assert.Empty(parsed.Payouts);
            Assert.Single(parsed.FailedRows);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void MalformedPlainTextCommaGroupingIsRejected()
        {
            var parsed = PayoutTextParser.ParseDetailed("6/5/2026 — Payment Batch payout Paid $1,23");
            Assert.Empty(parsed.Payouts);
            Assert.Single(parsed.FailedRows);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void NegativeForceVerifiedEarningIsRejectedInsteadOfMadePositive()
        {
            var parsed = PayoutTextParser.ParseDetailed("|6/6/2026|6/5/2026 12:00|Earning|Example — 1 min recording (force-verified)|Verified|-$1.50|");
            Assert.Empty(parsed.ForceVerifiedSessions);
            Assert.Single(parsed.FailedRows);
        }

        [Fact]
        public void ForceVerifiedEarningWithoutRecordedDateIsSkipped()
        {
            var parsed = PayoutTextParser.ParseDetailed("|6/6/2026|—|Earning|Example — 1 min recording (force-verified)|Verified|+$1.50|");
            Assert.Empty(parsed.ForceVerifiedSessions);
            Assert.Single(parsed.SkippedRows);
        }

        [Fact]
        public void InvalidHeaderAndDurationLeaveOtherRowsAvailableWithAccurateLineNumbers()
        {
            var parsed = PayoutTextParser.ParseDetailed("""
                |6/5/2026|—|Payment|Before|Paid|$1.00|

                |Posted|Recorded|Type|Description|Status|Amount|USD Amount|
                |6/5/2026|—|Payment|Ambiguous amount|Paid|$2.00|$3.00|
                |Posted|Recorded|Type|Description|Status|Amount|
                |6/6/2026|6/5/2026|Earning|Example — 2147483648 min recording (force-verified)|Verified|$1.00|
                |6/5/2026|—|Payment|After|Paid|$4.00|
                """);

            Assert.Equal(new[] { "Before", "After" }, parsed.Payouts.Select(payout => payout.Description));
            Assert.Empty(parsed.ForceVerifiedSessions);
            Assert.Collection(parsed.FailedRows,
                row => Assert.StartsWith("Line 3: duplicate", row),
                row => Assert.StartsWith("Line 4: could not read", row),
                row => Assert.StartsWith("Line 6: could not extract", row));
        }
    }
}
