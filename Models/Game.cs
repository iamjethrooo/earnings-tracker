using System;
using System.Collections.Generic;
using System.Text;

namespace nerve_earnings_tracker.Models
{
    public class Game
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal RatePerHour { get; set; }
        public decimal MinutesCap { get; set; }
        public DateTime? CapResetDate { get; set; }
        public bool IsArchived { get; set; }

    }
}
