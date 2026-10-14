namespace EarningsTracker.Models
{
    public class SessionChange
    {
        public int Id { get; set; }
        // The audit entry intentionally has no foreign key so it survives session deletion.
        public int SessionId { get; set; }
        public DateTime ChangedAt { get; set; }
        public string ChangeType { get; set; } = "Updated";
        public string BeforeValuesJson { get; set; } = "{}";
        public string AfterValuesJson { get; set; } = "{}";
        public string? SourceText { get; set; }
    }
}
