using EarningsTracker.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace EarningsTracker.Utilities
{
    public class ImportResult
    {
        internal Dictionary<int, SessionService.EditState> ExpectedSessionStates { get; } = [];
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
        public int UnlinkedCount => NewSessions.Count(session => !session.GameId.HasValue)
            + Updates.Count(update => !update.Imported.GameId.HasValue);

        public int TotalSessions =>
            InsertedCount + UpdatedCount + AmbiguousCount + InvalidCount + SkippedCount;

        public decimal TotalEarnings =>
            NewSessions.Sum(s => s.Earnings) +
            Updates.Sum(x => x.Imported.Earnings);
    }
}
