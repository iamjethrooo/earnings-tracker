using System.Globalization;
using System.Text.Json;
using nerve_earnings_tracker.Enums;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;

namespace nerve_earnings_tracker.Tests
{
    public class SessionEditingTests
    {
        private static Session Recording()
        {
            var recorded = new DateTime(2026, 6, 5, 12, 34, 56).AddTicks(1234567);
            return new Session
            {
                GameName = "Example", Date = recorded, RecordedDate = recorded,
                PostedDate = recorded.AddDays(2), Duration = TimeSpan.FromSeconds(61).Add(TimeSpan.FromTicks(7)),
                Earnings = 1.23m, Status = SessionStatus.Verified, IsForceVerified = true
            };
        }

        [Theory]
        [Trait("Category", "Regression")]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        public void SavingUntouchedDraftPreservesExactValues(string culture)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                var original = Recording();
                var item = new SessionItemViewModel(original);
                item.BeginEdit();
                // Calendar controls return dates without the original time component.
                item.DraftRecordedDate = original.RecordedDate.Date;
                item.DraftPostedDate = original.PostedDate!.Value.Date;
                Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
                Assert.Equal(original.Duration, edited.Duration);
                Assert.Equal(original.RecordedDate, edited.RecordedDate);
                Assert.Equal(original.Date, edited.Date);
                Assert.Equal(original.PostedDate, edited.PostedDate);
                Assert.Equal(original.Earnings, edited.Earnings);
                Assert.True(item.IsEditing);
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ChangingOnlyEarningsPreservesDurationAndRecordingIdentity()
        {
            var original = Recording();
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftEarnings = 2.50m.ToString(CultureInfo.CurrentCulture);
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(2.50m, edited.Earnings);
            Assert.Equal(original.Duration, edited.Duration);
            Assert.Equal(original.RecordedDate, edited.RecordedDate);
            Assert.Equal(original.PostedDate, edited.PostedDate);
            Assert.Equal(1.23m, original.Earnings);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void ChoosingAnotherDayPreservesRecordedAndPostedTimes()
        {
            var original = Recording();
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftRecordedDate = original.RecordedDate.Date.AddDays(1);
            item.DraftPostedDate = original.PostedDate!.Value.Date.AddDays(1);
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(original.RecordedDate.AddDays(1), edited.RecordedDate);
            Assert.Equal(edited.RecordedDate, edited.Date);
            Assert.Equal(original.PostedDate.Value.AddDays(1), edited.PostedDate);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void UntouchedEarningsPreserveExistingPrecision()
        {
            var original = Recording();
            original.Earnings = 1.2345m;
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(original.Earnings, edited.Earnings);
        }

        [Theory]
        [Trait("Category", "Regression")]
        [InlineData("79228162514264337593543950335")]
        [InlineData("15372286729")]
        public void OversizedDurationReturnsValidationErrorWithoutThrowing(string minutes)
        {
            var original = Recording();
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftDurationMinutes = minutes;
            Assert.False(item.TryCreateEditedSession(out _, out var error));
            Assert.Contains("duration", error, StringComparison.OrdinalIgnoreCase);
            Assert.True(item.IsEditing);
            Assert.Equal(TimeSpan.FromSeconds(61).Add(TimeSpan.FromTicks(7)), original.Duration);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not a number")]
        [InlineData("-1")]
        public void InvalidDurationIsRejectedWithoutChangingTheSession(string minutes)
        {
            var original = Recording();
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftDurationMinutes = minutes;
            Assert.False(item.TryCreateEditedSession(out _, out var error));
            Assert.NotEmpty(error);
            Assert.Equal(TimeSpan.FromSeconds(61).Add(TimeSpan.FromTicks(7)), original.Duration);
        }

        [Theory]
        [InlineData("en-US", "1.25", 750000000L)]
        [InlineData("de-DE", "1,25", 750000000L)]
        [InlineData("en-US", "0", 0L)]
        public void ChangedDurationUsesTheEditorsCulture(string culture, string minutes, long expectedTicks)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                var item = new SessionItemViewModel(Recording());
                item.BeginEdit();
                item.DraftDurationMinutes = minutes;
                Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
                Assert.Equal(expectedTicks, edited.Duration.Ticks);
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [Fact]
        public void CancelDoesNotChangeTheSessionAndNextEditReloadsSavedValues()
        {
            var original = Recording();
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftDurationMinutes = "999";
            item.DraftRecordedDate = original.RecordedDate.AddDays(5);
            item.ShowEditError("Test error");
            item.CancelEdit();
            Assert.False(item.IsEditing);
            Assert.Null(item.EditError);
            Assert.Equal(TimeSpan.FromSeconds(61).Add(TimeSpan.FromTicks(7)), original.Duration);
            item.BeginEdit();
            Assert.Equal(original.RecordedDate, item.DraftRecordedDate);
            Assert.Equal(original.Duration.TotalMinutes.ToString("0.##", CultureInfo.CurrentCulture), item.DraftDurationMinutes);
        }

        [Fact]
        public void UntouchedMaximumDurationRemainsValidDespiteDisplayRounding()
        {
            var original = Recording();
            original.Duration = TimeSpan.MaxValue;
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(TimeSpan.MaxValue, edited.Duration);
        }

        [Fact]
        public void ChangedDurationNearMaximumConvertsWithoutOverflow()
        {
            var item = new SessionItemViewModel(Recording());
            item.BeginEdit();
            var minutes = 15372286728.09129301m;
            item.DraftDurationMinutes = minutes.ToString(CultureInfo.CurrentCulture);
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(9223372036854775806L, edited.Duration.Ticks);
        }

        [Theory]
        [InlineData(DateTimeKind.Utc)]
        [InlineData(DateTimeKind.Local)]
        public void CalendarEditsPreserveTimestampKind(DateTimeKind kind)
        {
            var original = Recording();
            original.Date = original.RecordedDate = DateTime.SpecifyKind(original.RecordedDate, kind);
            original.PostedDate = DateTime.SpecifyKind(original.PostedDate!.Value, kind);
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftRecordedDate = original.RecordedDate.Date.AddDays(1);
            item.DraftPostedDate = original.PostedDate.Value.Date.AddDays(1);
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            Assert.Equal(kind, edited.RecordedDate.Kind);
            Assert.Equal(kind, edited.PostedDate!.Value.Kind);
            Assert.Equal(original.RecordedDate.TimeOfDay, edited.RecordedDate.TimeOfDay);
        }

        [Fact]
        [Trait("Category", "Regression")]
        public void SavingAnEarningsEditPersistsExactDurationTimestampAndAuditValues()
        {
            using var database = new TestDatabase();
            var original = Recording();
            var importer = new SessionImporter(database.Options);
            importer.ImportSessions(importer.PreviewImport([original]));
            var item = new SessionItemViewModel(original);
            item.BeginEdit();
            item.DraftRecordedDate = original.RecordedDate.Date;
            item.DraftPostedDate = original.PostedDate!.Value.Date;
            item.DraftEarnings = 2.50m.ToString(CultureInfo.CurrentCulture);
            Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
            SessionService.Update(original.Id, edited, database.Options);
            using var read = database.CreateContext();
            var saved = Assert.Single(read.Sessions);
            Assert.Equal(original.RecordedDate, saved.RecordedDate);
            Assert.Equal(original.Duration, saved.Duration);
            Assert.Equal(original.PostedDate, saved.PostedDate);
            Assert.Equal(2.50m, saved.Earnings);
            var changes = read.SessionChanges.OrderBy(change => change.Id).ToList();
            Assert.Equal(2, changes.Count);
            using var before = JsonDocument.Parse(changes[1].BeforeValuesJson);
            using var after = JsonDocument.Parse(changes[1].AfterValuesJson);
            Assert.Equal(1.23m, before.RootElement.GetProperty("Earnings").GetDecimal());
            Assert.Equal(2.50m, after.RootElement.GetProperty("Earnings").GetDecimal());
            Assert.Equal(original.RecordedDate, after.RootElement.GetProperty("RecordedDate").GetDateTime());
            Assert.Equal(original.Duration, TimeSpan.Parse(after.RootElement.GetProperty("Duration").GetString()!));
        }
    }
}
