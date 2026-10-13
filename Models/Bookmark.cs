using System;
using System.Collections.Generic;
using System.Text;

namespace nerve_earnings_tracker.Models
{
    public class Bookmark
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public Game Game { get; set; } = null!;
    }

}
