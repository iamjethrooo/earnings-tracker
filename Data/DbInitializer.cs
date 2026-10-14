using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.Json;
using EarningsTracker.Models;

namespace EarningsTracker.Data
{
    class DbInitializer
    {
        public static void Initialize(AppDbContext db, string? seedFilePath = null)
        {
            if (db.Games.Any())
                return;

            var path = seedFilePath ?? Path.Combine(AppContext.BaseDirectory, "Data", "games.json");

            if (!File.Exists(path))
                throw new FileNotFoundException($"The file '{path}' was not found.");

            var json = File.ReadAllText(path);
            var games = JsonSerializer.Deserialize<List<Game>>(json);

            if (games == null)
                return;

            // The non-null default on Game.Name must not make a missing seed name acceptable.
            if (games.Any(game => game == null || string.IsNullOrWhiteSpace(game.Name)))
                throw new InvalidDataException("The game seed contains a missing or empty name.");

            db.Games.AddRange(games);
            db.SaveChanges();
        }
    }
}
