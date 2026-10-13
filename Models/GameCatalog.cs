namespace nerve_earnings_tracker.Models;

public class GameMetadata
{
    public int GameId { get; set; }
    public string CatalogId { get; set; } = "";
    public long? IgdbId { get; set; }
    public string? ItadId { get; set; }
    public string Summary { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string CoverSha256 { get; set; } = "";
    public string Provenance { get; set; } = "Unresolved";
    public bool Reviewed { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class GameGenre
{
    public int GameId { get; set; }
    public string Name { get; set; } = "";
}

public enum AcquisitionKind { Purchase, FreeToPlay, Giveaway, Subscription, Unknown }

public class StoreOffer
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Source { get; set; } = "";
    public string Url { get; set; } = "";
    public string Edition { get; set; } = "Base game";
    public string Region { get; set; } = "PH";
    public string Currency { get; set; } = "PHP";
    public decimal? RegularPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public AcquisitionKind Kind { get; set; }
    public DateTime VerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Provenance { get; set; } = "";
}

public class SubscriptionAvailability
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Service { get; set; } = "";
    public string Region { get; set; } = "PH";
    public string Platform { get; set; } = "PC";
    public bool Included { get; set; }
    public DateTime VerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Url { get; set; } = "";
    public string Provenance { get; set; } = "";
}

public class GameOwnership
{
    public int GameId { get; set; }
    public bool IsOwned { get; set; }
    public string Source { get; set; } = "";
    public decimal? PurchaseAmount { get; set; }
    public string Currency { get; set; } = "PHP";
}

public class CatalogSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ExchangeRate
{
    public string Currency { get; set; } = "";
    // Units of this currency for one USD.
    public decimal PerUsd { get; set; }
    public DateTime Date { get; set; }
}
