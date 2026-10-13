using nerve_earnings_tracker.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace nerve_earnings_tracker.Utilities
{
    public class ImportResult
    {
        public List<Session> NewSessions { get; } = [];
        public List<(int Id, Session Imported)> Updates { get; } = [];
        public List<Session> AmbiguousSessions { get; } = [];
        public List<Session> InvalidSessions { get; } = [];
        public List<string> SkippedRows { get; } = [];

        public int InsertedCount => NewSessions.Count;
        public int UpdatedCount => Updates.Count;
        public int AmbiguousCount => AmbiguousSessions.Count;
        public int InvalidCount => InvalidSessions.Count;
        public int SkippedCount => SkippedRows.Count;

        public int TotalSessions =>
            InsertedCount + UpdatedCount + AmbiguousCount + InvalidCount + SkippedCount;

        public decimal TotalEarnings =>
            NewSessions.Sum(s => s.Earnings) +
            Updates.Sum(x => x.Imported.Earnings);
    }
}
