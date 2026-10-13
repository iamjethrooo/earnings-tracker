using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using nerve_earnings_tracker.Data;
using nerve_earnings_tracker.Models;
using nerve_earnings_tracker.Exceptions;

namespace nerve_earnings_tracker.Utilities
{
    internal static class PayoutService
    {
        public static event EventHandler? DataChanged;

        public static List<Payout> GetAll()
        {
            using var db = new AppDbContext();
            return db.Payouts.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToList();
        }

        public static Payout Create(Payout payout, string? reason = null, DbContextOptions<AppDbContext>? options = null)
        {
            Validate(payout);
            using var db = options == null ? new AppDbContext() : new AppDbContext(options);
            using var tx = db.Database.BeginTransaction();
            payout.Type = payout.Type.Trim();
            payout.Description = payout.Description.Trim();
            payout.Status = payout.Status.Trim();
            payout.StartDate = payout.Date;
            payout.EndDate = payout.Date;
            payout.ImportedAt = null;
            payout.SourceId = null;
            payout.SourceRow = null;
            payout.SourceValuesJson = null;
            if (payout.Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase) || payout.Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase))
                payout.ReconciliationState = payout.IsAdjustmentIncludedInSessionEarnings ? "IncludedInSessions" : "Separate";
            else
                payout.ReconciliationState = "Manual";
            payout.Changes.Add(new PayoutChange
            {
                ChangedAt = DateTime.Now,
                ChangeType = "Created",
                AfterValuesJson = Snapshot(payout),
                Reason = reason
            });
            db.Payouts.Add(payout);
            db.SaveChanges();
            tx.Commit();
            NotifyDataChanged();
            return payout;
        }

        public static void Update(int id, Payout edited, string? reason = null, DbContextOptions<AppDbContext>? options = null)
        {
            Validate(edited);
            using var db = options == null ? new AppDbContext() : new AppDbContext(options);
            using var tx = db.Database.BeginTransaction();
            var existing = db.Payouts.Single(p => p.Id == id);
            var before = Snapshot(existing);
            existing.Date = edited.Date;
            existing.Type = edited.Type.Trim();
            existing.Description = edited.Description.Trim();
            existing.Status = edited.Status.Trim();
            existing.Amount = edited.Amount;
            existing.IsAdjustmentIncludedInSessionEarnings = edited.IsAdjustmentIncludedInSessionEarnings;
            existing.ReconciliationState = edited.ReconciliationState;
            existing.Changes.Add(new PayoutChange
            {
                ChangedAt = DateTime.Now,
                ChangeType = "Updated",
                BeforeValuesJson = before,
                AfterValuesJson = Snapshot(existing),
                Reason = reason
            });
            db.SaveChanges();
            tx.Commit();
            NotifyDataChanged();
        }

        public static void SoftDeleteManual(int id, string? reason = null, DbContextOptions<AppDbContext>? options = null)
        {
            using var db = options == null ? new AppDbContext() : new AppDbContext(options);
            using var tx = db.Database.BeginTransaction();
            var payout = db.Payouts.Single(p => p.Id == id);
            if (payout.ImportedAt != null || payout.SourceRow != null || payout.SourceValuesJson != null)
                throw new InvalidOperationException("Imported payouts are kept in history and cannot be deleted.");
            var before = Snapshot(payout);
            payout.IsDeleted = true;
            payout.DeletedAt = DateTime.Now;
            payout.Changes.Add(new PayoutChange
            {
                ChangedAt = DateTime.Now,
                ChangeType = "Deleted",
                BeforeValuesJson = before,
                AfterValuesJson = Snapshot(payout),
                Reason = reason
            });
            db.SaveChanges();
            tx.Commit();
            NotifyDataChanged();
        }

        public static void Validate(Payout payout)
        {
            if (payout.Date == default)
                throw new ArgumentException("Enter a valid payout date.");
            if (string.IsNullOrWhiteSpace(payout.Type))
                throw new ArgumentException("Select a payout type.");
            if (string.IsNullOrWhiteSpace(payout.Description))
                throw new ArgumentException("Enter a description.");
            if (string.IsNullOrWhiteSpace(payout.Status))
                throw new ArgumentException("Select a status.");
            if (payout.Amount == 0m)
                throw new ArgumentException("Amount cannot be zero.");
            if (decimal.Round(payout.Amount, 2) != payout.Amount)
                throw new ArgumentException("Amount can have no more than two decimal places.");
            if ((payout.Type.Equals("Bonus", StringComparison.OrdinalIgnoreCase) || payout.Type.Equals("Earnings correction", StringComparison.OrdinalIgnoreCase)) && payout.ReconciliationState is not ("Separate" or "IncludedInSessions"))
                throw new ArgumentException("Reconcile this bonus or correction as separate from, or already included in, session earnings.");
        }

        internal static void NotifyDataChanged()
        {
            var handlers = DataChanged;
            if (handlers == null)
                return;

            var failures = new List<Exception>();
            // Persistence has committed. One broken display must not prevent other views refreshing.
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(null, EventArgs.Empty); }
                catch (Exception ex) { failures.Add(ex); }
            }
            if (failures.Count > 0)
            {
                var error = new PostCommitRefreshException(new AggregateException(failures));
                System.Diagnostics.Trace.TraceError(error.ToString());
                throw error;
            }
        }

        internal static string Snapshot(Payout p) => JsonSerializer.Serialize(new
        {
            p.Date, p.Type, p.Description, p.Status, p.Amount, p.SourceId, p.SourceRow,
            p.SourceValuesJson, p.ImportedAt, p.IsAdjustmentIncludedInSessionEarnings,
            p.ReconciliationState, p.IsDeleted, p.DeletedAt
        });
    }
}
