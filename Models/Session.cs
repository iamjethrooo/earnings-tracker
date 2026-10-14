using System;
using System.Collections.Generic;
using System.Text;
using EarningsTracker.Enums;

namespace EarningsTracker.Models
{
    public class Session
    {
        public int Id { get; set; }
        public string GameName { get; set; } = string.Empty;
        public int? GameId { get; set; }
        public Game? Game { get; set; }
        public int? PayoutId { get; set; }
        public Payout? Payout { get; set; }
        public DateTime Date { get; set; }
        // Date is retained for compatibility and mirrors the recording date.
        public DateTime RecordedDate { get; set; }
        public DateTime? PostedDate { get; set; }
        public bool IsForceVerified { get; set; }
        public TimeSpan Duration { get; set; }
        public SessionStatus Status { get; set; }
        public decimal Earnings { get; set; }

        // Transient parser evidence is persisted on the immutable import audit entry.
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? ImportSourceText { get; set; }

        public string DurationText
        {
            get
            {
                int totalSeconds = (int)Duration.TotalSeconds;

                int hours = totalSeconds / 3600;
                int minutes = (totalSeconds % 3600) / 60;
                int seconds = totalSeconds % 60;

                if (hours > 0)
                    return seconds == 0 ? $"{hours}h {minutes}m" : $"{hours}h {minutes}m {seconds}s";

                if (minutes > 0)
                    return seconds == 0 ? $"{minutes}m" : $"{minutes}m {seconds}s";

                if (minutes < 1)
                    return "<1m";
                return $"{seconds}s";
            }
        }

        public string EarningsText =>
            Status is SessionStatus.NotUploaded or SessionStatus.Rejected ? "" : Utilities.CurrencyDisplay.FormatUsd(Earnings);

        public string DateTimeText => Date.ToString("MMM d, yyyy h:mm tt");
    }
}
