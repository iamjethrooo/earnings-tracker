namespace EarningsTracker.Utilities;

internal static class CatalogSources
{
    // Direct platforms only; reseller offers remain stored for provenance.
    internal static string? PopularName(string source) => source.Trim().ToLowerInvariant() switch
    {
        "steam" => "Steam",
        "epic games" or "epic games store" => "Epic Games",
        "ubisoft" or "ubisoft store" or "ubisoft connect" => "Ubisoft Connect",
        "ea" or "ea store" or "ea app" or "origin" => "EA app",
        "microsoft store" or "xbox" or "xbox store" => "Microsoft Store",
        "gog" or "gog.com" => "GOG",
        "battle.net" or "battlenet" or "blizzard" => "Battle.net",
        "pc game pass" or "xbox game pass" or "game pass" => "PC Game Pass",
        "roblox" => "Roblox",
        "official website" => "Official website",
        _ => null
    };
}
