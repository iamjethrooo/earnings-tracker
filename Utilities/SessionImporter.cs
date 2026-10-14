using EarningsTracker.Data;
using EarningsTracker.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace EarningsTracker.Utilities
{
    internal class SessionImporter
    {

        private readonly DbContextOptions<AppDbContext>? _options;

        public SessionImporter(DbContextOptions<AppDbContext>? options = null)
        {
            _options = options;
        }

        private static List<Session> FindMatchingSessions(AppDbContext db, Session importedSession)
        {
            DateTime minuteStart = new(
                importedSession.Date.Year,
                importedSession.Date.Month,
                importedSession.Date.Day,
                importedSession.Date.Hour,
                importedSession.Date.Minute,
                0);

            DateTime minuteEnd = minuteStart.AddMinutes(1);

            var query = db.Sessions.Where(s => s.Date >= minuteStart && s.Date < minuteEnd);
            if (importedSession.GameId.HasValue)
                query = query.Where(s => s.GameId == importedSession.GameId
                    || s.GameName == importedSession.GameName);
            else
                query = query.Where(s => s.GameName == importedSession.GameName);
            return query.ToList();
        }

        private static bool UpdateSession(Session existing, Session imported)
        {
            var forceVerified = existing.IsForceVerified || imported.IsForceVerified;
            var postedDate = existing.PostedDate;
            if (imported.Status == Enums.SessionStatus.Verified && !existing.IsForceVerified)
                postedDate = imported.PostedDate ?? existing.PostedDate ?? (forceVerified ? null : imported.Date);

            // Decimal scale can change when SQLite reloads a value; compare values, not JSON text.
            var changed = existing.GameId != imported.GameId || existing.GameName != imported.GameName
                || existing.Duration != imported.Duration || existing.Date != imported.Date
                || existing.RecordedDate != imported.RecordedDate || existing.Earnings != imported.Earnings
                || existing.Status != imported.Status || existing.IsForceVerified != forceVerified
                || existing.PostedDate != postedDate;
            existing.GameId = imported.GameId;
            existing.GameName = imported.GameName;
            existing.Duration = imported.Duration;
            existing.Date = imported.Date;
            existing.RecordedDate = imported.RecordedDate;
            existing.Earnings = imported.Earnings;
            existing.Status = imported.Status;
            existing.IsForceVerified = forceVerified;
            existing.PostedDate = postedDate;
            return changed;
        }

        public void ImportSessions(ImportResult result)
        {
            // Preview records are editable; check the entire batch again before changing any data.
            foreach (var session in result.NewSessions)
                SessionService.ValidateImportedSession(session);
            foreach (var update in result.Updates)
                SessionService.ValidateImportedSession(update.Imported);

            using var db = _options == null ? new AppDbContext() : new AppDbContext(_options);
            using var transaction = db.Database.BeginTransaction();

            foreach (var session in result.NewSessions)
            {
                if (session.Status == Enums.SessionStatus.Verified && !session.IsForceVerified && session.PostedDate == null)
                    session.PostedDate = session.Date;
                db.Sessions.Add(session);
            }

            foreach (var update in result.Updates)
            {
                var existing = db.Sessions.Find(update.Id);
                if (existing == null)
                    throw new InvalidOperationException($"Session {update.Id} could not be found while applying the import.");

                var before = SessionService.Snapshot(existing);
                var changed = UpdateSession(existing, update.Imported);
                var after = SessionService.Snapshot(existing);
                // Older imports discarded their source. Enrich them once without inventing a financial change.
                var missingEvidence = !changed && update.Imported.ImportSourceText != null
                    && !db.SessionChanges.Any(change => change.SessionId == existing.Id && change.SourceText != null);
                if (changed || missingEvidence)
                {
                    db.SessionChanges.Add(new SessionChange
                    {
                        SessionId = existing.Id,
                        ChangedAt = DateTime.Now,
                        ChangeType = changed ? "Imported update" : "Imported evidence",
                        BeforeValuesJson = before,
                        AfterValuesJson = changed ? after : before,
                        SourceText = update.Imported.ImportSourceText
                    });
                }
            }

            db.SaveChanges();
            foreach (var session in result.NewSessions)
            {
                db.SessionChanges.Add(new SessionChange
                {
                    SessionId = session.Id,
                    ChangedAt = DateTime.Now,
                    ChangeType = "Imported",
                    AfterValuesJson = SessionService.Snapshot(session),
                    SourceText = session.ImportSourceText
                });
            }
            db.SaveChanges();
            transaction.Commit();
            PayoutService.NotifyDataChanged();
        }

        public ImportResult PreviewImport(IEnumerable<Session> importedSessions)
        {
            var result = new ImportResult();
            using var db = _options == null ? new AppDbContext() : new AppDbContext(_options);
            var targetedExistingIds = new HashSet<int>();
            var disputedExistingIds = new HashSet<int>();

            foreach (var group in importedSessions.GroupBy(GetImportKey, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var session in group)
                    SessionService.ValidateImportedSession(session);
                var importedSession = group.First();
                if (group.Any(session => !SameImportedSession(importedSession, session)))
                {
                    result.AmbiguousSessions.AddRange(group);
                    continue;
                }
                foreach (var duplicate in group.Skip(1))
                    result.SkippedRows.Add($"Repeated session row: {duplicate.GameName}, {duplicate.RecordedDate:g}, {duplicate.DurationText}.");

                var minuteMatches = FindMatchingSessions(db, importedSession);
                var matches = minuteMatches.Where(session => session.Date == importedSession.Date).ToList();
                // Same-name games can be different rate/cap versions of one Nerve game.
                // A different local link must neither overwrite the recording nor create earned income again.
                if (matches.Any(session => session.GameId.HasValue && session.GameId != importedSession.GameId))
                {
                    result.AmbiguousSessions.Add(importedSession);
                    disputedExistingIds.UnionWith(matches.Select(session => session.Id));
                    continue;
                }
                if (matches.Count == 0)
                {
                    if (minuteMatches.Count == 0)
                        result.NewSessions.Add(importedSession);
                    else
                        result.AmbiguousSessions.Add(importedSession);
                }
                else if (matches.Count == 1)
                {
                    if (targetedExistingIds.Add(matches[0].Id))
                        result.Updates.Add((matches[0].Id, importedSession));
                    else
                    {
                        result.AmbiguousSessions.Add(importedSession);
                        disputedExistingIds.Add(matches[0].Id);
                    }
                }
                else
                {
                    var refined = matches.Where(existing => existing.Duration == importedSession.Duration).ToList();
                    if (refined.Count == 1 && targetedExistingIds.Add(refined[0].Id))
                        result.Updates.Add((refined[0].Id, importedSession));
                    else
                    {
                        result.AmbiguousSessions.Add(importedSession);
                        disputedExistingIds.UnionWith(matches.Select(session => session.Id));
                    }
                }
            }

            // Competing links to one historical recording must not depend on incoming row order.
            foreach (var update in result.Updates.ToList())
            {
                if (!disputedExistingIds.Contains(update.Id))
                    continue;
                result.Updates.Remove(update);
                result.AmbiguousSessions.Add(update.Imported);
            }
            return result;
        }

        private static string GetImportKey(Session session) =>
            $"{session.GameName.Trim().ToUpperInvariant()}|{session.RecordedDate.Ticks}";

        private static bool SameImportedSession(Session first, Session second) =>
            first.GameId == second.GameId && first.Date == second.Date
            && first.RecordedDate == second.RecordedDate && first.PostedDate == second.PostedDate
            && first.Duration == second.Duration && first.Status == second.Status
            && first.Earnings == second.Earnings && first.IsForceVerified == second.IsForceVerified;
    }
}
