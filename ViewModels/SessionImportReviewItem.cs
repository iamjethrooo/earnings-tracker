using EarningsTracker.Enums;
using EarningsTracker.Models;
using EarningsTracker.Utilities;

namespace EarningsTracker.ViewModels;

// Read-only source review. The importer still owns validation and the eventual write.
public sealed record SessionImportReviewItem(string Change, string Recording, string Details, string Before, string Assignment)
{
    internal static IReadOnlyList<SessionImportReviewItem> Create(ImportResult result, IReadOnlyList<Game> games)
    {
        string Identity(int? id, string name)
        {
            var game = games.FirstOrDefault(game => game.Id == id);
            return game != null ? $"{game.Name} · {CurrencyDisplay.FormatUsd(game.RatePerHour)}/h · game #{game.Id}"
                : id.HasValue ? $"{name} · game #{id}" : $"{name} · unlinked";
        }
        string Assignment(bool isExplicit, int? payoutId) => result.PayoutOptions
            .FirstOrDefault(option => option.IsExplicit == isExplicit && option.PayoutId == payoutId)?.Label
            ?? (!isExplicit ? "Historical · inferred from posted date" : payoutId.HasValue ? $"Payment #{payoutId} · existing assignment" : "Current payout · awaiting payment");
        string SourceUsd(decimal amount) => "$" + amount.ToString("0.00##########################", System.Globalization.CultureInfo.InvariantCulture);
        string Details(Session session) => $"{Identity(session.GameId, session.GameName)}\n{session.DurationText} · {session.Status} · {SourceUsd(session.Earnings)}"
            + (session.AfkDuration.HasValue ? $" · AFK {session.AfkDuration.Value.TotalMinutes:0.##} min" : "");

        var rows = result.NewSessions.Select(session => new SessionImportReviewItem("New",
            $"{session.RecordedDate:MMM d, yyyy h:mm tt}", Details(session), "", Assignment(session.HasExplicitPayoutAssignment, session.AssignedPayoutId))).ToList();
        foreach (var (id, session) in result.Updates)
        {
            result.ExpectedSessionStates.TryGetValue(id, out var before);
            var previous = before == null ? "Saved values unavailable in this preview." :
                $"Before: {Identity(before.GameId, before.GameName)} · {before.RecordedDate:MMM d, yyyy h:mm tt} · {before.Duration.TotalMinutes:0.####} min · {before.Status} · {SourceUsd(before.Earnings)}";
            var newlyVerified = before != null && before.Status != SessionStatus.Verified && session.Status == SessionStatus.Verified;
            rows.Add(new("Update", $"Session #{id} · {session.RecordedDate:MMM d, yyyy h:mm tt}", Details(session), previous,
                before == null ? "Existing assignment reviewed at save" : Assignment(before.HasExplicitPayoutAssignment || newlyVerified, before.AssignedPayoutId)));
        }
        rows.AddRange(result.AmbiguousSessions.Select(session => new SessionImportReviewItem("Conflict · excluded",
            $"{session.RecordedDate:MMM d, yyyy h:mm tt}", Details(session), "", "Not saved")));
        rows.AddRange(result.InvalidSessions.Select(session => new SessionImportReviewItem("Invalid · excluded",
            $"{session.RecordedDate:MMM d, yyyy h:mm tt}", Details(session), "", "Not saved")));
        rows.AddRange(result.SkippedRows.Select(row => new SessionImportReviewItem("Repeated · skipped", row, "", "", "Not saved")));
        return rows;
    }
}
