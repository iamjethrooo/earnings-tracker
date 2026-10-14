using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.Tests;

[Collection("DataChanged")]
public class CapResetAuditTests
{
    private static int AddGame(TestDatabase database)
    {
        using var db = database.CreateContext();
        var game = new Game { Name = "Example", RatePerHour = 1.5m, MinutesCap = 60m };
        db.Games.Add(game);
        db.SaveChanges();
        return game.Id;
    }

    [Fact]
    public void ChangingAndClearingResetDateRetainsBeforeAfterContext()
    {
        using var database = new TestDatabase();
        var id = AddGame(database);
        var reset = new DateTime(2026, 6, 5);
        GameService.SaveCapResetDate(id, reset, database.Options);
        GameService.SaveCapResetDate(id, null, database.Options);
        using var db = database.CreateContext();
        var changes = db.GameChanges.OrderBy(change => change.Id).ToList();
        var beforeValues = changes.Select(change => change.BeforeValuesJson).ToList();
        var afterValues = changes.Select(change => change.AfterValuesJson).ToList();
        Assert.Equal(2, beforeValues.Count);
        Assert.All(changes, change =>
        {
            Assert.Equal(id, change.GameId);
            Assert.Equal("Cap reset changed", change.ChangeType);
            Assert.NotEqual(default, change.ChangedAt);
        });
        using var initial = JsonDocument.Parse(beforeValues[0]);
        using var changed = JsonDocument.Parse(afterValues[0]);
        using var cleared = JsonDocument.Parse(afterValues[1]);
        Assert.Equal(JsonValueKind.Null, initial.RootElement.GetProperty("CapResetDate").ValueKind);
        Assert.Equal(reset, changed.RootElement.GetProperty("CapResetDate").GetDateTime());
        Assert.Equal("Example", changed.RootElement.GetProperty("Name").GetString());
        Assert.Equal(60m, changed.RootElement.GetProperty("MinutesCap").GetDecimal());
        Assert.Equal(afterValues[0], beforeValues[1]);
        Assert.Equal(JsonValueKind.Null, cleared.RootElement.GetProperty("CapResetDate").ValueKind);
        Assert.Null(Assert.Single(db.Games).CapResetDate);
    }

    [Fact]
    public void UnchangedResetDateDoesNotCreateFalseAuditChanges()
    {
        using var database = new TestDatabase();
        var id = AddGame(database);
        GameService.SaveCapResetDate(id, new DateTime(2026, 6, 5), database.Options);
        GameService.SaveCapResetDate(id, new DateTime(2026, 6, 5, 12, 0, 0), database.Options);
        using var db = database.CreateContext();
        Assert.Single(db.GameChanges);
    }

    [Fact]
    public void FailedAuditInsertRollsBackResetDate()
    {
        using var database = new TestDatabase();
        var id = AddGame(database);
        using (var db = database.CreateContext())
            db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_game_audit BEFORE INSERT ON GameChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        Assert.Throws<DbUpdateException>(() => GameService.SaveCapResetDate(id, new DateTime(2026, 6, 5), database.Options));
        using var read = database.CreateContext();
        Assert.Null(Assert.Single(read.Games).CapResetDate);
        Assert.Empty(read.GameChanges);
    }

    [Fact]
    public void RefreshSeesCommittedHistoryAndIsNotSentWhenAuditFails()
    {
        using var database = new TestDatabase();
        var id = AddGame(database);
        var notifications = 0;
        void Changed(object? sender, EventArgs args)
        {
            using var read = database.CreateContext();
            Assert.Single(read.GameChanges);
            Assert.Equal(new DateTime(2026, 6, 5), Assert.Single(read.Games).CapResetDate);
            notifications++;
        }
        PayoutService.DataChanged += Changed;
        try
        {
            GameService.SaveCapResetDate(id, new DateTime(2026, 6, 5), database.Options);
            using (var db = database.CreateContext())
                db.Database.ExecuteSqlRaw("CREATE TRIGGER reject_notified_audit BEFORE INSERT ON GameChanges BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
            Assert.Throws<DbUpdateException>(() => GameService.SaveCapResetDate(id, null, database.Options));
            Assert.Equal(1, notifications);
        }
        finally
        {
            PayoutService.DataChanged -= Changed;
        }
    }

    [Fact]
    public void RemovingAGameRetainsItsHistoricalCapContext()
    {
        using var database = new TestDatabase();
        var id = AddGame(database);
        GameService.SaveCapResetDate(id, new DateTime(2026, 6, 5), database.Options);
        using (var db = database.CreateContext())
        {
            db.Games.Remove(Assert.Single(db.Games));
            db.SaveChanges();
        }
        using var read = database.CreateContext();
        Assert.Empty(read.Games);
        Assert.Equal(id, Assert.Single(read.GameChanges).GameId);
    }
}
