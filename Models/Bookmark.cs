using System;
using System.Collections.Generic;
using System.Text;

namespace EarningsTracker.Models
{
    public class Bookmark
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public Game Game { get; set; } = null!;
    }

}
