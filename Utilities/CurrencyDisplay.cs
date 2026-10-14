using System.Globalization;

namespace EarningsTracker.Utilities
{
    internal static class CurrencyDisplay
    {
        // Financial records are USD even when Windows uses another currency.
        internal static string FormatUsd(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    }
}
