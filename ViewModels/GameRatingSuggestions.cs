namespace EarningsTracker.ViewModels;

internal static class GameRatingSuggestions
{
    internal static void Apply(IReadOnlyCollection<GameItemViewModel> games)
    {
        var reviewed = games.Where(HasReviewedGenres)
            .Select(game => (Game: game, Genres: game.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();
        var rated = reviewed.Where(entry => entry.Game.GrindingRating != 0 || entry.Game.ReplayRating != 0).ToList();
        foreach (var game in games.Where(game => !HasReviewedGenres(game)))
            game.SetRatingSuggestions(default, 0, 0);

        foreach (var candidate in reviewed)
        {
            if (candidate.Game.GrindingRating != 0 && candidate.Game.ReplayRating != 0)
            {
                candidate.Game.SetRatingSuggestions(default, 0, 0);
                continue;
            }
            decimal grindingTotal = 0, grindingWeight = 0, replayTotal = 0, replayWeight = 0;
            int grindingSources = 0, replaySources = 0;
            foreach (var source in rated)
            {
                if (source.Game.Game.Id == candidate.Game.Game.Id) continue;
                var shared = source.Genres.Count(candidate.Genres.Contains);
                var union = candidate.Genres.Count + source.Genres.Count - shared;
                // Require at least half the combined tags to match. Closer matches carry more weight.
                if (shared * 2 < union) continue;
                var weight = (decimal)shared / union;
                if (source.Game.GrindingRating != 0 && candidate.Game.GrindingRating == 0)
                {
                    grindingTotal += source.Game.GrindingRating * weight;
                    grindingWeight += weight;
                    grindingSources++;
                }
                if (source.Game.ReplayRating != 0 && candidate.Game.ReplayRating == 0)
                {
                    replayTotal += source.Game.ReplayRating * weight;
                    replayWeight += weight;
                    replaySources++;
                }
            }
            candidate.Game.SetRatingSuggestions(new(ToRating(grindingTotal, grindingWeight), ToRating(replayTotal, replayWeight)),
                grindingSources, replaySources);
        }
    }

    private static bool HasReviewedGenres(GameItemViewModel game) => game.Metadata?.Reviewed == true && game.Genres.Count > 0;

    private static int ToRating(decimal total, decimal weight) => weight == 0 ? 0
        : (int)Math.Round(total / weight, MidpointRounding.AwayFromZero);
}
