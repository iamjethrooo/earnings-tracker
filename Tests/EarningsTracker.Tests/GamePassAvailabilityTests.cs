using EarningsTracker.Models;
using EarningsTracker.Utilities;
using EarningsTracker.ViewModels;

namespace EarningsTracker.Tests;

[Collection("Wpf")]
public class GamePassAvailabilityTests(WpfTestHost host)
{
    [Fact]
    public void VerificationBoundaryAndExpiryNeverExtendAvailability()
    {
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var entry = Entry(1, now.AddDays(-7));
        Assert.True(CatalogPresentation.Current(entry, now));
        Assert.False(CatalogPresentation.Current(entry, now.AddTicks(1)));
        Assert.True(CatalogPresentation.PreviouslyIncluded(entry, now.AddTicks(1)));
        entry.VerifiedAt = now;
        entry.ExpiresAt = now;
        Assert.False(CatalogPresentation.Current(entry, now));
        Assert.True(CatalogPresentation.PreviouslyIncluded(entry, now));
        entry.VerifiedAt = now.AddDays(1);
        Assert.False(CatalogPresentation.PreviouslyIncluded(entry, now));
        entry.VerifiedAt = default;
        Assert.False(CatalogPresentation.PreviouslyIncluded(entry, now));
        Assert.Contains("verification date not recorded", CatalogPresentation.SubscriptionText(entry, now));
    }

    [Fact]
    public Task StaleGamePassFilterRetainsEvidenceWithoutGrantingPlayableAccess() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        Seed(database, now);
        var model = new GamesViewModel(database.Options) { GamePassOnly = true };
        Assert.Equal(new[] { 1, 2, 3, 9, 10, 11 }, model.GamesView.Cast<GameItemViewModel>().Select(item => item.Game.Id).Order());
        var stale = model.Games.Single(item => item.Game.Id == 2);
        Assert.Contains("Game Pass availability may have changed.", stale.GamePassStatus);
        Assert.Contains(now.AddDays(-8).ToString("yyyy-MM-dd HH:mm"), stale.GamePassStatus);
        Assert.True(stale.GamePassNeedsVerification);
        Assert.False(stale.GamePassCurrentlyAvailable);
        model.SaveSubscriptions(["PC Game Pass"]);
        model.PlayableOnly = true;
        Assert.Equal(new[] { 1, 9, 10 }, model.GamesView.Cast<GameItemViewModel>().Select(item => item.Game.Id).Order());
        model.SaveSubscriptions([]);
        Assert.Equal(new[] { 9, 10 }, model.GamesView.Cast<GameItemViewModel>().Select(item => item.Game.Id).Order());
        model.PlayableOnly = false;
        model.ShowArchived = false;
        Assert.DoesNotContain(model.GamesView.Cast<GameItemViewModel>(), item => item.Game.Id == 11);
        model.ReloadCatalog();
        Assert.Contains(model.GamesView.Cast<GameItemViewModel>(), item => item.Game.Id == 2);
        var settings = new SettingsViewModel(database.Options);
        Assert.Contains("1 currently verified available", settings.SubscriptionStatus);
        Assert.Contains("4 previously included, verification stale/expired", settings.SubscriptionStatus);
        Assert.Contains("2 currently verified not included", settings.SubscriptionStatus);
        Assert.Contains("Stale verification does not confirm removal", settings.SubscriptionStatus);
        using var db = database.CreateContext();
        Assert.Equal(now.AddDays(-8), db.SubscriptionAvailability.Single(entry => entry.GameId == 2).VerifiedAt);
        Assert.Equal(now.AddHours(-1), db.SubscriptionAvailability.Single(entry => entry.GameId == 3).ExpiresAt);
        Assert.Equal(1234.56m, db.GameOwnership.Single().PurchaseAmount);
        Assert.Empty(db.Sessions); Assert.Empty(db.SessionChanges); Assert.Empty(db.GameChanges);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RecordedRemovalReplacesStaleInclusionWithoutErasingEvidenceDates() => host.RunAsync(() =>
    {
        using var database = new TestDatabase();
        var now = DateTime.UtcNow;
        Seed(database, now);
        var model = new GamesViewModel(database.Options) { GamePassOnly = true };
        var original = model.Games.Single(item => item.Game.Id == 2);
        using (var db = database.CreateContext())
        {
            var entry = db.SubscriptionAvailability.Single(entry => entry.GameId == 2);
            entry.Included = false; entry.VerifiedAt = now;
            db.SaveChanges();
        }
        model.ReloadCatalog();
        Assert.DoesNotContain(model.GamesView.Cast<GameItemViewModel>(), item => item.Game.Id == 2);
        Assert.Same(original, model.Games.Single(item => item.Game.Id == 2));
        Assert.Contains("Not included · verification current", original.GamePassStatus);
        Assert.False(original.GamePassCurrentlyAvailable);
        Assert.DoesNotContain("Game Pass availability may have changed.", original.GamePassStatus);
        return Task.CompletedTask;
    });

    internal static void Seed(TestDatabase database, DateTime now)
    {
        using var db = database.CreateContext();
        db.Games.AddRange(Enumerable.Range(1, 12).Select(id => new Game
        {
            Id = id, Name = $"Synthetic availability {id}", RatePerHour = 1.25m, MinutesCap = 120,
            IsArchived = id == 11
        }));
        db.SaveChanges();
        db.SubscriptionAvailability.AddRange(
            Entry(1, now.AddDays(-1)), Entry(2, now.AddDays(-8)),
            Entry(3, now.AddDays(-1), expires: now.AddHours(-1)), Entry(4, now.AddDays(-1), included: false),
            Entry(5, now.AddDays(-9), included: false), Entry(6, now.AddDays(-1), platform: "Console"),
            Entry(7, now.AddDays(1)), Entry(8, default), Entry(9, now.AddDays(-8)), Entry(10, now.AddDays(-8)),
            Entry(11, now.AddDays(-8)), Entry(12, now.AddDays(-8)), Entry(12, now.AddDays(-1), included: false));
        db.GameOwnership.Add(new GameOwnership { GameId = 9, IsOwned = true, Source = "Steam", PurchaseAmount = 1234.56m, Currency = "PHP" });
        var offer = CatalogTests.Offer(); offer.GameId = 10; offer.SalePrice = 0;
        db.StoreOffers.Add(offer);
        db.SaveChanges();
    }

    internal static SubscriptionAvailability Entry(int game, DateTime verified, bool included = true, DateTime? expires = null, string platform = "PC") => new()
    {
        GameId = game, Service = "PC Game Pass", Platform = platform, Region = "PH", Included = included,
        VerifiedAt = verified, ExpiresAt = expires, Url = "https://example.com/game-pass", Provenance = "Synthetic reviewed evidence"
    };
}
