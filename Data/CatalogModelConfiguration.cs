using Microsoft.EntityFrameworkCore;
using EarningsTracker.Models;

namespace EarningsTracker.Data;

internal static class CatalogModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<GameMetadata>().HasKey(x => x.GameId);
        model.Entity<GameMetadata>().HasIndex(x => x.CatalogId).IsUnique();
        model.Entity<GameGenre>().HasKey(x => new { x.GameId, x.Name });
        model.Entity<GameOwnership>().HasKey(x => x.GameId);
        model.Entity<CatalogSetting>().HasKey(x => x.Key);
        model.Entity<ExchangeRate>().HasKey(x => x.Currency);
        model.Entity<StoreOffer>().HasIndex(x => x.GameId);
        model.Entity<SubscriptionAvailability>().HasIndex(x => x.GameId);
        model.Entity<GameMetadata>().HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<GameGenre>().HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<GameOwnership>().HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<StoreOffer>().HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SubscriptionAvailability>().HasOne<Game>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
    }
}
