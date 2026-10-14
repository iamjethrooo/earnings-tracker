using System;
using System.Collections.Generic;
using System.Text;

namespace EarningsTracker.Models
{
    public class Game
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal RatePerHour { get; set; }
        public decimal MinutesCap { get; set; }
        public DateTime? CapResetDate { get; set; }
        public bool IsArchived { get; set; }

    }
}
