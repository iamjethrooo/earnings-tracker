using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text.Json;
using nerve_earnings_tracker.Models;

namespace nerve_earnings_tracker.Data
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

            db.Games.AddRange(games);
            db.SaveChanges();
        }
    }
}
