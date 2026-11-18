using System.Text.Json;
using EarningsTracker.Exceptions;
using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;

namespace EarningsTracker.Tests;

[Trait("Category", "Regression")]
public class SessionAfkImportTests
{
    private static List<Session> ParseRecording(List<Game> games, string deduction = "") =>
        SessionTextParser.Parse($"Example\nExample · Jun 5, 2026 12:00 PM · 1h 0m 0s\n+$3.00\n{deduction}\nVerified", games, null);

    [Fact]
    public void EarningsMatchUsesEligibleDurationForDuplicateGameNames()
    {
        List<Game> games =
        [
            new() { Id = 1, Name = "Example", RatePerHour = 3m },
            new() { Id = 2, Name = "Example", RatePerHour = 4m }
        ];

        var session = Assert.Single(ParseRecording(games, "15 min AFK deducted"));
        Assert.Equal(2, session.GameId);
        Assert.Equal(TimeSpan.FromHours(1), session.Duration);
        Assert.Equal(TimeSpan.FromMinutes(45), session.CapEligibleDuration);
        Assert.Equal(3m, session.Earnings);
    }

    [Fact]
    public void GameDetailsParsesTheDeductionAndKeepsRecordingSeconds()
    {
        var game = new Game { Id = 1, Name = "Example" };
        var session = Assert.Single(SessionTextParser.Parse(
            "Nerve\nrecording-20260605120000000.mkv\n45m 20s\n+$3.00\n7 min AFK deducted\nVerified", [game], game));

        Assert.Equal(game.Id, session.GameId);
        Assert.Equal(TimeSpan.FromMinutes(7), session.AfkDuration);
        Assert.Equal(new TimeSpan(0, 38, 20), session.CapEligibleDuration);
    }

    [Theory]
    [InlineData("-7 min AFK deducted")]
    [InlineData("sixty min AFK deducted")]
    [InlineData("61 min AFK deducted")]
    [InlineData("7 min AFK deducted\n8 min AFK deducted")]
    public void InvalidDeductionIsReportedWithItsLineNumber(string deduction)
    {
        var error = Assert.Throws<InvalidSessionFormatException>(() =>
            ParseRecording([new Game { Name = "Example" }], deduction));
        Assert.Contains("Line ", error.Message);
        Assert.Contains("AFK deduction", error.Message);
    }

    [Fact]
    public void ReimportAuditsTheDeductionOnceAndRetainsItWhenLaterOmitted()
    {
        using var database = new TestDatabase();
        var game = new Game { Name = "Example" };
        using (var seed = database.CreateContext()) { seed.Games.Add(game); seed.SaveChanges(); }
        var importer = new SessionImporter(database.Options);
        const string deduction = "7 min AFK deducted";

        importer.ImportSessions(importer.PreviewImport(ParseRecording([game])));
        var olderPreview = importer.PreviewImport(ParseRecording([game]));
        importer.ImportSessions(importer.PreviewImport(ParseRecording([game], deduction)));
        Assert.Throws<InvalidOperationException>(() => importer.ImportSessions(olderPreview));
        importer.ImportSessions(importer.PreviewImport(ParseRecording([game], deduction)));
        importer.ImportSessions(importer.PreviewImport(ParseRecording([game])));

        using var read = database.CreateContext();
        var session = Assert.Single(read.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(7), session.AfkDuration);
        Assert.Equal(TimeSpan.FromHours(1), session.Duration);
        Assert.Equal(3m, session.Earnings);
        Assert.Equal(2, read.SessionChanges.Count());
        var update = Assert.Single(read.SessionChanges, change => change.ChangeType == "Imported update");
        Assert.Contains(deduction, update.SourceText);
        using var before = JsonDocument.Parse(update.BeforeValuesJson);
        using var after = JsonDocument.Parse(update.AfterValuesJson);
        Assert.Equal(JsonValueKind.Null, before.RootElement.GetProperty("AfkDuration").ValueKind);
        Assert.Equal("00:07:00", after.RootElement.GetProperty("AfkDuration").GetString());

        var item = new SessionItemViewModel(session);
        item.BeginEdit();
        item.DraftDurationMinutes = "6";
        Assert.False(item.TryCreateEditedSession(out _, out _));
        Assert.Equal(nameof(SessionItemViewModel.DraftDurationMinutes), item.InvalidField);
        item.DraftDurationMinutes = "50";
        Assert.True(item.TryCreateEditedSession(out var edited, out var error), error);
        SessionService.Update(session.Id, edited, database.Options, item.EditState);
        Assert.Equal(TimeSpan.FromMinutes(43), edited.CapEligibleDuration);
        using var reopened = database.CreateContext();
        Assert.Equal(TimeSpan.FromMinutes(43), Assert.Single(reopened.Sessions).CapEligibleDuration);
    }
}
