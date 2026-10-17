using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EarningsTracker.Data;
using EarningsTracker.Enums;
using EarningsTracker.Models;

namespace EarningsTracker.Utilities
{
    internal static class SessionService
    {
        public static void Update(int id, Session edited, DbContextOptions<AppDbContext>? options = null, EditState? expectedState = null)
        {
            Validate(edited);
            using var db = options == null ? new AppDbContext() : new AppDbContext(options);
            using var transaction = db.Database.BeginTransaction();
            var existing = db.Sessions.SingleOrDefault(session => session.Id == id)
                ?? throw new InvalidOperationException("This session no longer exists. Reload sessions before editing again.");
            if (expectedState != null && CaptureEditState(existing) != expectedState)
                throw new InvalidOperationException("This session changed while you were editing. Cancel the draft and edit the refreshed session again.");
            var before = Snapshot(existing);

            // Historical recordings may be unlinked. Link explicitly without recalculating
            // their earnings using the game's current rate or changing existing links.
            Game? linkedGame = null;
            if (!existing.GameId.HasValue && edited.GameId.HasValue)
            {
                linkedGame = db.Games.SingleOrDefault(game => game.Id == edited.GameId.Value)
                    ?? throw new ArgumentException("The selected game no longer exists. Reload sessions and choose another game.");
                existing.GameId = linkedGame.Id;
                existing.GameName = linkedGame.Name;
            }

            existing.Date = edited.RecordedDate;
            existing.RecordedDate = edited.RecordedDate;
            existing.PostedDate = edited.PostedDate;
            existing.Duration = edited.Duration;
            existing.Status = edited.Status;
            existing.Earnings = edited.Earnings;
            existing.IsForceVerified = edited.IsForceVerified;
            if (existing.Status == SessionStatus.Verified && !existing.PostedDate.HasValue)
                existing.PostedDate = existing.RecordedDate;

            db.SessionChanges.Add(new SessionChange
            {
                SessionId = existing.Id,
                ChangedAt = DateTime.Now,
                ChangeType = "Updated",
                BeforeValuesJson = before,
                AfterValuesJson = Snapshot(existing)
            });
            db.SaveChanges();
            transaction.Commit();
            if (linkedGame != null)
            {
                edited.GameId = linkedGame.Id;
                edited.GameName = linkedGame.Name;
                edited.Game = linkedGame;
            }
            PayoutService.NotifyDataChanged();
        }

        public static void Delete(int id, DbContextOptions<AppDbContext>? options = null)
        {
            using var db = options == null ? new AppDbContext() : new AppDbContext(options);
            using var transaction = db.Database.BeginTransaction();
            var session = db.Sessions.Single(existing => existing.Id == id);
            var before = Snapshot(session);
            db.SessionChanges.Add(new SessionChange
            {
                SessionId = session.Id,
                ChangedAt = DateTime.Now,
                ChangeType = "Deleted",
                BeforeValuesJson = before,
                AfterValuesJson = "{}"
            });
            db.Sessions.Remove(session);
            db.SaveChanges();
            transaction.Commit();
            PayoutService.NotifyDataChanged();
        }

        // Value equality avoids false conflicts from decimal scale changes after SQLite reloads.
        internal sealed record EditState(int? GameId, string GameName, int? PayoutId, DateTime Date,
            DateTime RecordedDate, DateTime? PostedDate, TimeSpan Duration, SessionStatus Status,
            decimal Earnings, bool IsForceVerified);

        internal static EditState CaptureEditState(Session s) => new(s.GameId, s.GameName, s.PayoutId,
            s.Date, s.RecordedDate, s.PostedDate, s.Duration, s.Status, s.Earnings, s.IsForceVerified);

        internal static string Snapshot(Session session) => JsonSerializer.Serialize(new
        {
            session.GameId,
            session.GameName,
            session.Date,
            session.RecordedDate,
            session.PostedDate,
            session.Duration,
            session.Status,
            session.Earnings,
            session.IsForceVerified
        });

        internal static void ValidateImportedSession(Session session)
        {
            // Historical force verification may have no known posting date; never invent one.
            Validate(session, requireForceVerificationDate: false);
            if (session.Date != session.RecordedDate)
                throw new ArgumentException("The session date must match its recorded date.");
        }

        private static void Validate(Session session, bool requireForceVerificationDate = true)
        {
            if (session.RecordedDate == default)
                throw new ArgumentException("Enter a valid recorded date.");
            if (session.PostedDate.HasValue && session.PostedDate.Value == default)
                throw new ArgumentException("Enter a valid posted date.");
            if (session.Duration < TimeSpan.Zero)
                throw new ArgumentException("Duration cannot be negative.");
            ValidateEarnings(session.Earnings);
            if (!Enum.IsDefined(session.Status))
                throw new ArgumentException("Enter a valid session status.");
            if (requireForceVerificationDate && session.Status == SessionStatus.Verified && session.IsForceVerified && !session.PostedDate.HasValue)
                throw new ArgumentException("Enter a posted date for a force-verified session.");
        }

        internal static void ValidateEarnings(decimal earnings)
        {
            if (earnings < 0m)
                throw new ArgumentException("Session earnings cannot be negative. Record a correction as an earnings adjustment.");
        }
    }
}
