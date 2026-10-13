namespace nerve_earnings_tracker.Models
{
    public class PayoutChange
    {
        public int Id { get; set; }
        public int PayoutId { get; set; }
        public Payout? Payout { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangeType { get; set; } = "Updated";
        public string BeforeValuesJson { get; set; } = "{}";
        public string AfterValuesJson { get; set; } = "{}";
        public string? Reason { get; set; }
    }
}
