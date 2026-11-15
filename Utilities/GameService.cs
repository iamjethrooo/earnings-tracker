using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Models;
using System.Text.Json;

namespace EarningsTracker.Utilities
{
    internal static class GameService
    {
        // The caller owns the transaction that also commits the catalog links and audit entries.
        internal static Dictionary<string, Game> AddFromCatalog(AppDbContext db,
            List<CatalogService.RosterCandidate> additions, string version)
        {
            var added = additions.ToDictionary(candidate => candidate.CatalogId, candidate => new Game
            {
                Name = candidate.Name,
                RatePerHour = candidate.Terms.RatePerHour,
                MinutesCap = candidate.Terms.MinutesCap
            }, StringComparer.Ordinal);
            if (added.Count == 0) return added;

            db.Games.AddRange(added.Values);
            db.SaveChanges();
            var changedAt = DateTime.Now;
            foreach (var game in added.Values)
            {
                db.GameChanges.Add(new GameChange
                {
                    GameId = game.Id,
                    ChangedAt = changedAt,
                    ChangeType = $"Added from catalog {version}",
                    AfterValuesJson = Snapshot(game)
                });
            }
            return added;
        }

        internal static void ApplyBundledRosterUpdate(AppDbContext db)
        {
            string[] titles = ["MAVRIX", "Predecessor", "Wreckreation"];
            using var transaction = db.Database.BeginTransaction();
            var existing = db.Games
                .Where(game => titles.Contains(EF.Functions.Collate(game.Name, "NOCASE")))
                .ToLookup(game => game.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var title in titles)
            {
                if (!existing.Contains(title))
                    db.Games.Add(new Game { Name = title, RatePerHour = 2m, MinutesCap = 3000m });
            }

            // Update the old bundled entry in place only when its identity and terms are unambiguous.
            var predecessors = existing["Predecessor"].ToList();
            if (predecessors.Count == 1 && predecessors[0] is { IsArchived: false, RatePerHour: 1.5m, MinutesCap: 3000m } predecessor)
            {
                var before = Snapshot(predecessor);
                predecessor.RatePerHour = 2m;
                db.GameChanges.Add(new GameChange
                {
                    GameId = predecessor.Id,
                    ChangedAt = DateTime.Now,
                    ChangeType = "Bundled rate changed",
                    BeforeValuesJson = before,
                    AfterValuesJson = Snapshot(predecessor)
                });
            }

            db.SaveChanges();
            transaction.Commit();
        }

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
