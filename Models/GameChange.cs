namespace nerve_earnings_tracker.Models
{
    public class GameChange
    {
        public int Id { get; set; }
        // Keep historical cap context even if the game is subsequently removed.
        public int GameId { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangeType { get; set; } = "Cap reset changed";
        public string BeforeValuesJson { get; set; } = "{}";
        public string AfterValuesJson { get; set; } = "{}";
    }
}
