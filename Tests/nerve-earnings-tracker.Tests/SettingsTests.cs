using System.Net;
using System.Net.Http;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;

namespace nerve_earnings_tracker.Tests;

[Collection("Catalog updates")]
public class SettingsTests
{
    [Fact]
    public async Task DisabledAutomaticUpdatesMakeNoRequestsAndCanBeReenabled()
    {
        using var database = new TestDatabase();
        var settings = new AppSettings(database.Options);
        settings.Set("updates.automatic", "false");
        var handler = new CatalogTests.FakeHandler(_ => new(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var updates = new CatalogUpdates(database.Options, client);
        Assert.Equal("", await updates.RefreshAsync(false));
        Assert.Equal(0, handler.Calls);
        Assert.Equal("", settings.Get("updates.lastAttempt"));
        // Automatic checks never read the user's live-price credentials.
        settings.Set("updates.automatic", "true");
        Assert.Contains("keeping saved data", await updates.RefreshAsync(false));
        Assert.Equal(2, handler.Calls);
        Assert.NotEmpty(settings.Get("updates.lastAttempt"));
        Assert.Empty(settings.Get("updates.catalogSuccess"));
        Assert.Contains("keeping saved data", settings.Get("updates.result"));
    }

    [Fact]
    public void SettingsPersistAndDiagnosticsExcludeUnrecognizedSensitiveData()
    {
        using var database = new TestDatabase();
        var model = new SettingsViewModel(database.Options);
        model.SetGamePass(true); model.SetAutomaticUpdates(false);
        var reopened = new SettingsViewModel(database.Options);
        Assert.True(reopened.GamePass); Assert.False(reopened.AutomaticUpdates);
        var settings = new AppSettings(database.Options);
        settings.Set("secret.key", "sensitive-value");
        settings.Set("updates.result", "sensitive-value Exchange rates unavailable; keeping saved rates.");
        var report = reopened.Diagnostics();
        Assert.DoesNotContain("sensitive-value", report);
        Assert.Contains("keeping saved rates", report);
        Assert.Contains("PHP", report);
        reopened.SetGamePass(false);
        Assert.Empty(settings.Subscriptions());
    }
}
