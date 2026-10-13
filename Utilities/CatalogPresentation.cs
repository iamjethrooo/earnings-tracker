using nerve_earnings_tracker.Models;
using System.Globalization;

namespace nerve_earnings_tracker.Utilities;

internal static class CatalogPresentation
{
    internal static decimal? EffectivePrice(StoreOffer offer, DateTime now)
    {
        if (offer.Region != "PH" || offer.Kind != AcquisitionKind.Purchase) return null;
        return offer.ExpiresAt <= now ? offer.RegularPrice : offer.SalePrice ?? offer.RegularPrice;
    }

    internal static bool IsFree(StoreOffer offer, DateTime now) => offer.Region == "PH"
        && ((offer.Kind == AcquisitionKind.Purchase && EffectivePrice(offer, now) == 0)
            || ((offer.Kind == AcquisitionKind.FreeToPlay || (offer.Kind == AcquisitionKind.Giveaway && offer.ExpiresAt > now))
                && (offer.ExpiresAt == null || offer.ExpiresAt > now)));

    internal static bool Current(SubscriptionAvailability sub, DateTime now) => sub.Included && sub.Region == "PH"
        && sub.Platform == "PC" && sub.VerifiedAt <= now && sub.VerifiedAt >= now.AddDays(-7)
        && (sub.ExpiresAt == null || sub.ExpiresAt > now);

    internal static decimal? ToUsd(decimal amount, string currency, IReadOnlyDictionary<string, ExchangeRate> rates)
        => currency == "USD" ? amount : rates.TryGetValue(currency, out var rate) && rate.PerUsd > 0 ? amount / rate.PerUsd : null;

    internal static decimal? ToPhp(decimal amount, string currency, IReadOnlyDictionary<string, ExchangeRate> rates)
        => currency == "PHP" ? amount : ToUsd(amount, currency, rates) is decimal usd && rates.TryGetValue("PHP", out var rate)
            && rate.PerUsd > 0 ? usd * rate.PerUsd : null;

    internal static string Money(decimal amount, string currency) => currency == "PHP" ? $"₱{amount:N2}"
        : currency == "USD" ? $"US${amount:N2}" : $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    internal static string OfferText(StoreOffer offer, IReadOnlyDictionary<string, ExchangeRate> rates, DateTime now)
    {
        string label = offer.Kind switch
        {
            AcquisitionKind.FreeToPlay => "Free-to-play",
            AcquisitionKind.Giveaway => offer.ExpiresAt > now ? $"Giveaway until {offer.ExpiresAt:MMM d}" : "Giveaway expired / unverified",
            AcquisitionKind.Subscription => "Subscription required",
            _ => EffectivePrice(offer, now) is decimal price ? price == 0 ? "Free-to-play" : Money(price, offer.Currency) : "Price unknown"
        };
        if (offer.Currency != "PHP" && EffectivePrice(offer, now) is decimal value && value != 0 && ToPhp(value, offer.Currency, rates) is decimal php)
            label += $" (≈ ₱{php:N2})";
        return $"{offer.Source} · {label} · {offer.Edition} · checked {offer.VerifiedAt:MMM d, yyyy}";
    }

    internal static string CompactPrice(StoreOffer offer, DateTime now)
    {
        if (IsFree(offer, now)) return offer.Kind == AcquisitionKind.Giveaway ? "Temporary giveaway" : "Free-to-play";
        if (offer.Kind == AcquisitionKind.Subscription) return "Subscription access";
        if (EffectivePrice(offer, now) is not decimal price) return "Price unknown";
        var label = Money(price, offer.Currency);
        return price == decimal.Truncate(price) && label.EndsWith(".00") ? label[..^3] : label;
    }

    internal static string BreakEven(Game game, GameOwnership? ownership, IReadOnlyDictionary<string, ExchangeRate> rates)
    {
        if (ownership?.PurchaseAmount is not decimal cost) return "Enter a purchase cost to estimate break-even time.";
        if (game.RatePerHour <= 0) return "Break-even unavailable: no hourly earnings rate.";
        if (ToUsd(cost, ownership.Currency, rates) is not decimal usd) return "Break-even unavailable: exchange rate missing.";
        var hours = usd / game.RatePerHour;
        var rateDate = ownership.Currency == "USD" ? "" : $" · FX {rates[ownership.Currency].Date:MMM d, yyyy}";
        return $"Estimated break-even: {hours:0.##} hours{rateDate}"
            + (hours > game.MinutesCap / 60m ? " · exceeds full-cycle cap" : "")
            + ". Based on the current hourly rate; subscription fees excluded.";
    }
}
