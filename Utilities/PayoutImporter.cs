using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;

namespace nerve_earnings_tracker.Utilities
{
    internal class PayoutImporter : IDisposable
    {
        private readonly AppDbContext _db;
        public PayoutImporter(AppDbContext db) => _db = db;

        public PayoutImportResult PreviewImport(PayoutParseResult parsed)
        {
            var result = new PayoutImportResult();
            result.FailedRows.AddRange(parsed.FailedRows);
            result.SkippedRows.AddRange(parsed.SkippedRows);

            var importedSessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in parsed.ForceVerifiedSessions)
            {
                SessionService.ValidateEarnings(session.Earnings);
                var sessionKey = $"{Normalize(session.GameName)}|{session.RecordedDate:yyyyMMddHHmm}";
                if (importedSessions.TryGetValue(sessionKey, out var previousSession))
                {
                    if (previousSession.PostedDate == session.PostedDate && previousSession.Earnings == session.Earnings)
                        result.SkippedRows.Add($"Repeated force-verified session row: {session.GameName} ({session.RecordedDate:d} {session.RecordedDate:t}).");
                    else
                        result.ConflictingRows.Add($"Conflicting force-verified rows for {session.GameName} ({session.RecordedDate:d} {session.RecordedDate:t}).");
                    continue;
                }
                importedSessions.Add(sessionKey, session);
                var start = new DateTime(session.RecordedDate.Year, session.RecordedDate.Month, session.RecordedDate.Day, session.RecordedDate.Hour, session.RecordedDate.Minute, 0);
                var end = start.AddMinutes(1);
                var matches = _db.Sessions.Where(s => s.GameName == session.GameName && s.Date >= start && s.Date < end).ToList();
                if (matches.Count == 1)
                    result.ForceVerifiedSessionUpdates.Add((matches[0].Id, session));
                else if (matches.Count > 1)
                    result.AmbiguousForceVerifiedSessions.Add(session);
                else
                    result.UnmatchedForceVerifiedSessions.Add(session);
            }

            var importedKeys = new Dictionary<string, Payout>(StringComparer.OrdinalIgnoreCase);
            foreach (var payout in parsed.Payouts)
            {
                var key = BuildImportKey(payout);
                if (importedKeys.TryGetValue(key, out var priorRow))
                {
                    if (SameTransaction(priorRow, payout))
                        result.SkippedRows.Add($"Repeated row in this import: {payout.Type} — {payout.Description} ({payout.Date:d}).");
                    else
                        result.ConflictingRows.Add($"Conflicting rows share the same source identity: {payout.Type} — {payout.Description} ({payout.Date:d}) with {CurrencyDisplay.FormatUsd(priorRow.Amount)}/{priorRow.Status} and {CurrencyDisplay.FormatUsd(payout.Amount)}/{payout.Status}.");
                    continue;
                }
                importedKeys.Add(key, payout);

                List<Payout> candidates;
                if (payout.SourceId != null)
                {
                    candidates = _db.Payouts.Where(p => p.SourceId == payout.SourceId).ToList();
                    if (candidates.Count == 0)
                    {
                        // An export may add an identifier to a transaction imported without one.
                        candidates = _db.Payouts.Where(p => p.SourceId == null).ToList()
                            .Where(p => p.Date.Date == payout.Date.Date
                                && Normalize(p.Type) == Normalize(payout.Type)
                                && Normalize(p.Description) == Normalize(payout.Description)).ToList();
                        if (candidates.Count > 0 && (candidates.Count != 1 || !SameTransaction(candidates[0], payout)))
                        {
                            result.AmbiguousPayoutMatches.Add((payout, candidates));
                            continue;
                        }
                    }
                }
                else
                {
                    var sameType = _db.Payouts.Where(p => p.Type.ToLower() == payout.Type.ToLower()).ToList();
                    var sameDate = sameType.Where(p => p.Date.Date == payout.Date.Date).ToList();
                    var sameDescription = sameType.Where(p => Normalize(p.Description) == Normalize(payout.Description)).ToList();
                    candidates = sameDescription.Count > 0 ? sameDescription : sameDate;
                }

                if (payout.SourceId != null && candidates.Count > 1)
                {
                    result.AmbiguousPayoutMatches.Add((payout, candidates));
                    continue;
                }
                var exact = candidates.Where(p => SameTransaction(p, payout)).ToList();
                if (exact.Count == 1)
                {
                    if (payout.SourceId != null && exact[0].SourceId == null)
                        result.Updates.Add((exact[0].Id, exact[0], payout));
                    else
                        result.Duplicates.Add(payout);
                    continue;
                }
                if (candidates.Count == 0)
                {
                    payout.ImportedAt = DateTime.Now;
                    payout.ReconciliationState = IsAdjustment(payout) ? "NeedsReview" : "Imported";
                    payout.IsAdjustmentIncludedInSessionEarnings = false;
                    result.NewPayouts.Add(payout);
                    continue;
                }
                if (payout.SourceId != null && candidates.Count == 1)
                {
                    result.Updates.Add((candidates[0].Id, candidates[0], payout));
                    continue;
                }
                if (payout.SourceId == null)
                {
                    // Repeated descriptions across dates do not establish transaction identity.
                    var descriptionMatches = candidates.Where(p => p.Date.Date == payout.Date.Date
                        && Normalize(p.Description) == Normalize(payout.Description)).ToList();
                    if (descriptionMatches.Count == 1)
                    {
                        result.Updates.Add((descriptionMatches[0].Id, descriptionMatches[0], payout));
                        continue;
                    }
                }
                result.AmbiguousPayoutMatches.Add((payout, candidates));
            }

            // No row may be applied if another incoming identity competes for its target.
            var disputedIds = result.AmbiguousPayoutMatches
                .SelectMany(match => match.Candidates).Select(payout => payout.Id).ToHashSet();
            foreach (var group in result.Updates.GroupBy(update => update.Id))
                if (group.Count() > 1)
                    disputedIds.Add(group.Key);
            foreach (var update in result.Updates.ToList())
            {
                if (!disputedIds.Contains(update.Id))
                    continue;
                result.Updates.Remove(update);
                result.AmbiguousPayoutMatches.Add((update.Imported, [update.Existing]));
            }
            return result;
        }

        public void ImportPayouts(PayoutImportResult result)
        {
            if (result.ConflictingRows.Count > 0)
                throw new InvalidOperationException("Resolve conflicting source rows before importing.");
            if ((result.AmbiguousPayoutMatches.Count > 0 || result.AmbiguousForceVerifiedSessions.Count > 0) && !result.SkipAmbiguousRecords)
                throw new InvalidOperationException("Resolve all ambiguous matches before importing.");

            foreach (var update in result.ForceVerifiedSessionUpdates)
                SessionService.ValidateEarnings(update.Session.Earnings);

            using var transaction = _db.Database.BeginTransaction();
            foreach (var payout in result.NewPayouts)
            {
                payout.ImportedAt = DateTime.Now;
                payout.StartDate = payout.Date;
                payout.EndDate = payout.Date;
                payout.Changes.Add(new PayoutChange
                {
                    ChangedAt = DateTime.Now,
                    ChangeType = "Imported",
                    AfterValuesJson = PayoutService.Snapshot(payout),
                    Reason = "Imported from Nerve transaction history"
                });
                _db.Payouts.Add(payout);
            }

            foreach (var update in result.Updates)
            {
                var existing = _db.Payouts.SingleOrDefault(p => p.Id == update.Id)
                    ?? throw new InvalidOperationException($"Payout {update.Id} no longer exists. Import was not committed.");
                var before = PayoutService.Snapshot(existing);
                existing.Date = update.Imported.Date;
                existing.Type = update.Imported.Type;
                existing.Description = update.Imported.Description;
                existing.Status = update.Imported.Status;
                existing.Amount = update.Imported.Amount;
                existing.SourceId ??= update.Imported.SourceId;
                existing.SourceRow = update.Imported.SourceRow;
                existing.SourceValuesJson = update.Imported.SourceValuesJson;
                existing.ImportedAt = DateTime.Now;
                existing.Changes.Add(new PayoutChange
                {
                    ChangedAt = DateTime.Now,
                    ChangeType = "Imported update",
                    BeforeValuesJson = before,
                    AfterValuesJson = PayoutService.Snapshot(existing),
                    Reason = "Source transaction changed"
                });
            }

            foreach (var update in result.ForceVerifiedSessionUpdates)
            {
                var session = _db.Sessions.Find(update.Id)
                    ?? throw new InvalidOperationException($"Session {update.Id} no longer exists. Import was not committed.");
                var before = SessionService.Snapshot(session);
                var preservePostedDate = session.IsForceVerified && session.Status == Enums.SessionStatus.Verified;
                var postedDate = preservePostedDate ? session.PostedDate : update.Session.PostedDate;
                // JSON decimal formatting can differ after SQLite reloads an unchanged amount.
                var changed = session.Status != Enums.SessionStatus.Verified || !session.IsForceVerified
                    || session.PostedDate != postedDate || session.Earnings != update.Session.Earnings;
                session.Status = Enums.SessionStatus.Verified;
                session.PostedDate = postedDate;
                session.IsForceVerified = true;
                session.Earnings = update.Session.Earnings;
                var after = SessionService.Snapshot(session);
                var missingEvidence = !changed && update.Session.ImportSourceText != null
                    && !_db.SessionChanges.Any(change => change.SessionId == session.Id && change.SourceText != null);
                if (changed || missingEvidence)
                {
                    _db.SessionChanges.Add(new SessionChange
                    {
                        SessionId = session.Id,
                        ChangedAt = DateTime.Now,
                        ChangeType = changed ? "Payout import update" : "Imported evidence",
                        BeforeValuesJson = before,
                        AfterValuesJson = changed ? after : before,
                        SourceText = update.Session.ImportSourceText
                    });
                }
            }
            _db.SaveChanges();
            transaction.Commit();
            PayoutService.NotifyDataChanged();
        }

        private static bool IsAdjustment(Payout p) => p.Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase) || p.Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase);
        private static string Normalize(string value) => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        private static string BuildImportKey(Payout p) => p.SourceId != null ? $"ID:{p.SourceId}" : $"{p.Date:yyyyMMdd}|{Normalize(p.Type)}|{Normalize(p.Description)}";
        private static bool SameTransaction(Payout existing, Payout imported)
        {
            return existing.Date.Date == imported.Date.Date
                && Normalize(existing.Type) == Normalize(imported.Type)
                && Normalize(existing.Description) == Normalize(imported.Description)
                && existing.Status.Equals(imported.Status, StringComparison.OrdinalIgnoreCase)
                && existing.Amount == imported.Amount;
        }
        public void Dispose() => _db.Dispose();
    }
}
