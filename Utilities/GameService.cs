using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using System.Text.Json;

namespace nerve_earnings_tracker.Utilities
{
    internal static class GameService
    {
        internal static void SaveCapResetDate(int gameId, DateTime? resetDate,
            DbContextOptions<AppDbContext>? databaseOptions = null)
        {
            resetDate = resetDate?.Date;
            if (resetDate > DateTime.Today)
                throw new ArgumentException("The cap reset date cannot be in the future.");

            using var db = databaseOptions == null ? new AppDbContext() : new AppDbContext(databaseOptions);
            var game = db.Games.Find(gameId)
                ?? throw new InvalidOperationException("This game could not be found.");
            if (game.CapResetDate != resetDate)
            {
                var before = Snapshot(game);
                game.CapResetDate = resetDate;
                db.GameChanges.Add(new GameChange
                {
                    GameId = game.Id,
                    ChangedAt = DateTime.Now,
                    BeforeValuesJson = before,
                    AfterValuesJson = Snapshot(game)
                });
            }
            // One SaveChanges commits the cap date and its audit entry atomically.
            db.SaveChanges();
            PayoutService.NotifyDataChanged();
        }

        private static string Snapshot(Game game) => JsonSerializer.Serialize(new
        {
            game.Name,
            game.RatePerHour,
            game.MinutesCap,
            game.CapResetDate,
            game.IsArchived
        });
    }
}
