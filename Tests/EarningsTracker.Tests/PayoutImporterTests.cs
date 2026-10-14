using Microsoft.EntityFrameworkCore;
using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests
{
    public class PayoutImporterTests
    {
        private static PayoutParseResult Parse(string rows) => PayoutTextParser.ParseDetailed(rows);

        [Fact]
        public void ReimportingPaymentsAndAdjustmentsDoesNotAddRowsOrAuditEntries()
        {
            using var database = new TestDatabase();
            const string input = """
                |6/5/2026|—|Payment|Batch payout|Paid|-$10.00|
                |6/6/2026|—|Bonus|Bug reports|Verified|+$20.00|
                |6/7/2026|—|Earnings correction|Cap correction|Verified|-$0.01|
                """;
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse(input)));
            using (var importer = new PayoutImporter(database.CreateContext()))
            {
                var preview = importer.PreviewImport(Parse(input));
                Assert.Equal(3, preview.UnchangedCount);
                Assert.Equal(0, preview.AddedCount);
                Assert.Equal(0, preview.UpdatedCount);
                importer.ImportPayouts(preview);
            }
            using var read = database.CreateContext();
            Assert.Equal(3, read.Payouts.Count());
            Assert.Equal(3, read.PayoutChanges.Count());
            Assert.Equal(-10m, read.Payouts.Single(p => p.Type == "Payment").Amount);
            Assert.Equal(10m, PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalPaidOut);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void RepeatedDescriptionOnAnotherDateIsNotAnAutomaticUpdate()
        {
            using var database = new TestDatabase();
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
            using (var importer = new PayoutImporter(database.CreateContext()))
            {
                var preview = importer.PreviewImport(Parse("|6/12/2026|—|Payment|Batch payout|Paid|-$12.00|"));
                Assert.Empty(preview.Updates);
                Assert.Single(preview.AmbiguousPayoutMatches);
                Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(preview));
            }
            using var read = database.CreateContext();
            Assert.Equal(-10m, Assert.Single(read.Payouts).Amount);
            Assert.Single(read.PayoutChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void TwoIncomingRowsCannotAutomaticallyTargetOnePayout()
        {
            using var database = new TestDatabase();
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
            using var previewImporter = new PayoutImporter(database.CreateContext());
            var preview = previewImporter.PreviewImport(Parse("""
                |6/12/2026|—|Payment|Batch payout|Paid|-$12.00|
                |6/19/2026|—|Payment|Batch payout|Paid|-$14.00|
                """));
            Assert.Empty(preview.Updates);
            Assert.Equal(2, preview.AmbiguousPayoutMatches.Count);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void AddingSourceIdToAnExactIdlessRecordDoesNotDuplicateIt()
        {
            using var database = new TestDatabase();
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
            const string identified = """
                |Posted|Recorded|Type|Description|Status|Amount|Source ID|
                |6/5/2026|—|Payment|Batch payout|Paid|-$10.00|PAY-1|
                """;
            using (var importer = new PayoutImporter(database.CreateContext()))
            {
                var preview = importer.PreviewImport(Parse(identified));
                Assert.Empty(preview.NewPayouts);
                importer.ImportPayouts(preview);
            }
            using var read = database.CreateContext();
            Assert.Equal("PAY-1", Assert.Single(read.Payouts).SourceId);
            Assert.Equal(10m, PayoutAccounting.Calculate([], read.Payouts.ToList()).TotalPaidOut);
            using var reimporter = new PayoutImporter(database.CreateContext());
            Assert.Equal(1, reimporter.PreviewImport(Parse(identified)).UnchangedCount);
        }

        [Fact]
        public void StableSourceIdAllowsLegitimateAmountStatusAndDateUpdates()
        {
            using var database = new TestDatabase();
            const string header = "|Posted|Recorded|Type|Description|Status|Amount|Source ID|\n";
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse(header + "|6/5/2026|—|Payment|Batch payout|Processing|$10.00|PAY-1|")));
            using (var importer = new PayoutImporter(database.CreateContext()))
            {
                var preview = importer.PreviewImport(Parse(header + "|6/6/2026|—|Payment|Batch payout|Paid|-$12.00|PAY-1|"));
                Assert.Equal(1, preview.UpdatedCount);
                importer.ImportPayouts(preview);
            }
            using var read = database.CreateContext();
            var payment = Assert.Single(read.Payouts);
            Assert.Equal(new DateTime(2026, 6, 6), payment.Date);
            Assert.Equal("Paid", payment.Status);
            Assert.Equal(-12m, payment.Amount);
            Assert.Equal(2, read.PayoutChanges.Count());
        }

        [Theory]
        [Trait("Category", "Regression")]
        [InlineData("PAY-1", "PAY-2")]
        [InlineData("PAY-1", "—")]
        public void DifferentSourceIdentitiesCannotUpdateTheSameExistingRecord(string firstId, string secondId)
        {
            using var database = new TestDatabase();
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|")));
            using var previewImporter = new PayoutImporter(database.CreateContext());
            var preview = previewImporter.PreviewImport(Parse(
                "|Posted|Recorded|Type|Description|Status|Amount|Source ID|\n"
                + $"|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|{firstId}|\n"
                + $"|6/5/2026|—|Payment|Batch payout|Processing|$12.00|{secondId}|"));
            Assert.Empty(preview.Updates);
            Assert.Equal(2, preview.AmbiguousPayoutMatches.Count);
            Assert.Throws<InvalidOperationException>(() => previewImporter.ImportPayouts(preview));
            using var read = database.CreateContext();
            Assert.Equal(-10m, Assert.Single(read.Payouts).Amount);
            Assert.Single(read.PayoutChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ConflictingRowsBlockCommitEvenWhenAmbiguousRowsAreSkipped()
        {
            using var database = new TestDatabase();
            using var importer = new PayoutImporter(database.CreateContext());
            var preview = importer.PreviewImport(Parse("""
                |6/5/2026|—|Payment|Batch payout|Paid|-$10.00|
                |6/5/2026|—|Payment|Batch payout|Paid|-$12.00|
                """));
            Assert.Single(preview.ConflictingRows);
            preview.SkipAmbiguousRecords = true;
            Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(preview));
            using var read = database.CreateContext();
            Assert.Empty(read.Payouts);
            Assert.Empty(read.PayoutChanges);
        }

        [Fact]
        public void DuplicateSourceIdsRequireResolution()
        {
            using var database = new TestDatabase();
            using (var seed = database.CreateContext())
            {
                seed.Payouts.AddRange(new Payout { Type = "Payment", Status = "Paid", Description = "One", SourceId = "DUP", Amount = -1m },
                    new Payout { Type = "Payment", Status = "Paid", Description = "Two", SourceId = "DUP", Amount = -2m });
                seed.SaveChanges();
            }
            using var importer = new PayoutImporter(database.CreateContext());
            var preview = importer.PreviewImport(Parse("|Posted|Recorded|Type|Description|Status|Amount|Source ID|\n|6/5/2026|—|Payment|One|Paid|-$1.00|DUP|"));
            Assert.Single(preview.AmbiguousPayoutMatches);
            Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(preview));
        }

        [Fact]
        public void ForceVerificationUpdatesExistingSessionWithoutCreatingAnEarningRow()
        {
            using var database = new TestDatabase();
            var recorded = new DateTime(2026, 6, 5, 12, 0, 0);
            using (var seed = database.CreateContext())
            {
                seed.Sessions.Add(new Session { GameName = "Example", Date = recorded, RecordedDate = recorded,
                    Duration = TimeSpan.FromMinutes(1), Status = SessionStatus.Pending });
                seed.SaveChanges();
            }
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse("|6/6/2026|6/5/2026 12:00|Earning|Example — 1 min recording (force-verified)|Verified|+$1.50|")));
            using var read = database.CreateContext();
            var session = Assert.Single(read.Sessions);
            Assert.Equal(SessionStatus.Verified, session.Status);
            Assert.True(session.IsForceVerified);
            Assert.Equal(new DateTime(2026, 6, 6), session.PostedDate);
            Assert.Equal(1.50m, session.Earnings);
            Assert.Empty(read.Payouts);
            Assert.Single(read.SessionChanges);
        }

        [Fact]
        public void FailedPayoutImportRollsBackPayoutAndAuditTogether()
        {
            using var database = new TestDatabase();
            using var importer = new PayoutImporter(database.CreateContext());
            var preview = importer.PreviewImport(Parse("|6/5/2026|—|Payment|Batch payout|Paid|-$10.00|"));
            preview.ForceVerifiedSessionUpdates.Add((999, new Session()));
            Assert.Throws<InvalidOperationException>(() => importer.ImportPayouts(preview));
            using var read = database.CreateContext();
            Assert.Empty(read.Payouts);
            Assert.Empty(read.PayoutChanges);
            Assert.Empty(read.SessionChanges);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ReimportingForceVerificationPreservesPostedDateAndDoesNotAddFalseAuditChanges()
        {
            using var database = new TestDatabase();
            var date = new DateTime(2026, 6, 5, 12, 0, 0);
            using (var seed = database.CreateContext())
            {
                seed.Sessions.Add(new Session { GameName = "Example", Date = date, RecordedDate = date,
                    Duration = TimeSpan.FromMinutes(1), Status = SessionStatus.Pending });
                seed.SaveChanges();
            }
            const string row = "|6/6/2026|6/5/2026 12:00|Earning|Example — 1 min recording (force-verified)|Verified|+$1.50|";
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse(row)));
            using (var importer = new PayoutImporter(database.CreateContext()))
                importer.ImportPayouts(importer.PreviewImport(Parse(row.Replace("6/6/2026", "6/10/2026"))));
            using var read = database.CreateContext();
            var saved = Assert.Single(read.Sessions);
            Assert.Equal(new DateTime(2026, 6, 6), saved.PostedDate);
            Assert.Equal(1.50m, saved.Earnings);
            Assert.Single(read.SessionChanges);
        }
    }
}
