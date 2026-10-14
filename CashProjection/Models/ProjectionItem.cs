namespace CashProjection.Models
{
    public enum Periodicity
    {
        NotApplicable,
        Monthly,
        Quarterly,
        SemiAnnually,
        Annually,
        TwoWeeksPastLast,
    }

    // Pure domain model (POCO) – no UI dependencies, no INPC, no parsing.
    // Represents a user-entered recurring/scheduled bill or deposit (as opposed
    // to a real BankTransaction downloaded from Plaid).
    public sealed class ProjectionItem
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; } = DateTime.Now.Date;
        public decimal? Deposit { get; set; }
        public decimal? Payment { get; set; }
        public Periodicity Periodicity { get; set; }
        public string? MatchText { get; set; }
        public bool NeedsReview { get; set; }
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
