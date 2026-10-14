using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EarningsTracker.Utilities
{
    public class PayoutImportResult
    {
        public List<Payout> NewPayouts { get; set; } = [];
        public List<Payout> Duplicates { get; set; } = [];
        public List<(int Id, Payout Existing, Payout Imported)> Updates { get; set; } = [];
        public List<string> SkippedRows { get; set; } = [];
        public List<string> FailedRows { get; set; } = [];
        public List<string> ConflictingRows { get; set; } = [];
        public List<(Payout Imported, List<Payout> Candidates)> AmbiguousPayoutMatches { get; set; } = [];
        public List<(int Id, Session Session)> ForceVerifiedSessionUpdates { get; set; } = [];
        public List<Session> AmbiguousForceVerifiedSessions { get; set; } = [];
        public List<Session> UnmatchedForceVerifiedSessions { get; set; } = [];
        public bool SkipAmbiguousRecords { get; set; }

        public int UnchangedCount => Duplicates.Count;
        public int AddedCount => NewPayouts.Count;
        public int UpdatedCount => Updates.Count + ForceVerifiedSessionUpdates.Count;
        public int SkippedCount => SkippedRows.Count + ConflictingRows.Count + AmbiguousPayoutMatches.Count + AmbiguousForceVerifiedSessions.Count + UnmatchedForceVerifiedSessions.Count;
    }
}
