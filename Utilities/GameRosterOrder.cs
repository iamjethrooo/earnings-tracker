using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities;

internal static class GameRosterOrder
{
    private sealed record Entry(string Name, decimal RatePerHour, decimal MinutesCap,
        bool IsArchived, string[]? Aliases);
    private readonly record struct Terms(string Name, decimal RatePerHour, decimal MinutesCap);
    private sealed record OrderLookup(Dictionary<(Terms Terms, bool IsArchived), int> Exact, Dictionary<Terms, int> UniqueTerms);
    private static readonly Lazy<OrderLookup> Orders = new(ReadOrders);

    internal static long GetNewestOrder(Game game)
    {
        var terms = new Terms(NormalizeName(game.Name), game.RatePerHour, game.MinutesCap);
        var orders = Orders.Value;
        if (orders.Exact.TryGetValue((terms, game.IsArchived), out var order)
            || orders.UniqueTerms.TryGetValue(terms, out order))
            return order;

        // Future catalog additions and custom games retain newest-ID order ahead of this snapshot.
        return (long)int.MaxValue + game.Id;
    }

    private static OrderLookup ReadOrders()
    {
        using var stream = typeof(GameRosterOrder).Assembly
            .GetManifestResourceStream("EarningsTracker.Data.game-order.json")
            ?? throw new InvalidDataException("The bundled game order is missing.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream)
            ?? throw new InvalidDataException("The bundled game order is empty.");
        var exact = new Dictionary<(Terms Terms, bool IsArchived), int>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            foreach (var name in new[] { entry.Name }.Concat(entry.Aliases ?? []))
            {
                var terms = new Terms(NormalizeName(name), entry.RatePerHour, entry.MinutesCap);
                exact.Add((terms, entry.IsArchived), entries.Count - index);
            }
        }

        // An archive toggle must not lose a unique version's position. Repeated archived/active
        // terms (such as Enlisted) still require an exact match rather than guessing an identity.
        var uniqueTerms = exact.GroupBy(pair => pair.Key.Terms).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Value);
        return new(exact, uniqueTerms);
    }

    private static string NormalizeName(string name) => Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();
}
