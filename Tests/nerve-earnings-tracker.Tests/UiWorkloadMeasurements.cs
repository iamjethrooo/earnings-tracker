using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Utilities;
using nerve_earnings_tracker.ViewModels;
using nerve_earnings_tracker.Views;
using System.Diagnostics;
using System.IO;

namespace nerve_earnings_tracker.Tests;

// Repeatable local observations, not wall-clock pass/fail thresholds or production benchmarks.
[Collection("Wpf")]
public class UiWorkloadMeasurements(WpfTestHost host)
{
    [Fact]
    public Task MeasureLargeReadAndPreviewWorkloads() => host.RunAsync(async () =>
    {
        var results = new List<string>();
        var timer = Stopwatch.StartNew();
        using var database = new TestDatabase();
        results.Add($"Fresh isolated migration: {timer.ElapsedMilliseconds} ms");
        using (var db = database.CreateContext())
        {
            db.Games.AddRange(Enumerable.Range(0, 1000).Select(i => new Game { Name = $"Game {i}", RatePerHour = 1 }));
            db.Sessions.AddRange(Enumerable.Range(0, 5000).Select(i => new Session
            { GameName = $"Game {i % 1000}", Date = DateTime.Today.AddMinutes(-i), RecordedDate = DateTime.Today.AddMinutes(-i) }));
            db.Payouts.AddRange(Enumerable.Range(0, 1000).Select(i => new Payout
            { Date = DateTime.Today.AddDays(-i), Type = "Payment", Description = $"Payment {i}", Status = "Paid", Amount = -1 }));
            db.SaveChanges();
        }
        timer.Restart();
        var model = new GamesViewModel(database.Options);
        results.Add($"1,000-game construction/catalog: {timer.ElapsedMilliseconds} ms");
        timer.Restart();
        foreach (var text in new[] { "G", "Ga", "Gam", "Game", "Game ", "Game 1", "Game 10", "Game 100", "", "Game" }) model.SearchGames(text);
        results.Add($"10 successive 1,000-game search updates: {timer.ElapsedMilliseconds} ms");
        timer.Restart();
        var payments = new PayoutsView(database.Options);
        await payments.RefreshHistory();
        results.Add($"5,000-session/1,000-payment history: {timer.ElapsedMilliseconds} ms");
        var sessionText = string.Join("\n", Enumerable.Range(0, 1000).Select(i =>
            $"Game 0\nvideo.mkv · {new DateTime(2026, 6, 1).AddMinutes(i):M/d/yyyy h:mm tt} · 1m\n+$1.00\nVerified"));
        timer.Restart();
        var parsed = SessionTextParser.Parse(sessionText, model.Games.Select(item => item.Game).ToList(), null);
        var preview = new SessionImporter(database.Options).PreviewImport(parsed);
        results.Add($"1,000-row session parse/preview: {timer.ElapsedMilliseconds} ms ({preview.TotalSessions} rows)");
        var payoutText = string.Join("\n", Enumerable.Range(0, 1000).Select(i =>
            $"|6/5/2026|—|Payment|Preview {i}|Paid|-$1.00|"));
        timer.Restart();
        using (var importer = new PayoutImporter(database.CreateContext()))
        {
            var payoutPreview = importer.PreviewImport(PayoutTextParser.ParseDetailed(payoutText));
            results.Add($"1,000-row payout parse/preview: {timer.ElapsedMilliseconds} ms ({payoutPreview.AddedCount} rows)");
        }
        var path = Path.Combine(AppContext.BaseDirectory, "phase3-workloads.txt");
        await File.WriteAllLinesAsync(path, results);
        using var read = database.CreateContext();
        Assert.Equal(5000, read.Sessions.Count());
        Assert.Equal(1000, read.Payouts.Count());
    });
}
