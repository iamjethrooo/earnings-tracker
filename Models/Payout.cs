using System;
using System.Collections.Generic;
using System.Text;

namespace nerve_earnings_tracker.Models
{
    public class Payout
    {
        public int Id { get; set; }
        // These legacy range columns remain required in existing SQLite databases.
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? SourceId { get; set; }
        public string? SourceValuesJson { get; set; }
        public string? SourceRow { get; set; }
        public DateTime? ImportedAt { get; set; }
        public bool IsAdjustmentIncludedInSessionEarnings { get; set; }
        public string ReconciliationState { get; set; } = "Unreconciled";
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public ICollection<PayoutChange> Changes { get; set; } = new List<PayoutChange>();
    } 
}
