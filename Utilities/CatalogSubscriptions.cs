namespace nerve_earnings_tracker.Utilities;

internal static class CatalogSubscriptions
{
    internal static readonly string[] Services = ["PC Game Pass"];

    internal static bool Includes(IEnumerable<string> selected, string service)
    {
        var memberships = selected.ToHashSet(StringComparer.Ordinal);
        return service == "PC Game Pass" && memberships.Contains(service);
    }
}
