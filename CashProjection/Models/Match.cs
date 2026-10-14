namespace CashProjection.Models
{
    public enum MatchStatus
    {
        Presumptive,
        Confirmed,
        Rejected,
    }

    // A presumptive or decided link between a ProjectionItem and a real
    // BankTransaction found by the fuzzy-matching engine.
    public sealed class Match
    {
        public int Id { get; set; }
        public int ProjectionItemId { get; set; }
        public int BankTransactionId { get; set; }
        public MatchStatus Status { get; set; }
        public int Score { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? DecidedAtUtc { get; set; }

        // Snapshot of the projection item's TransactionDate at the moment
        // this match was confirmed. Once the item is rescheduled to a new
        // occurrence, its live date no longer equals this — which is how we
        // know to stop treating the (now-different) live item as part of
        // this match's visual pairing, while the bank transaction itself
        // stays permanently "crossed off" regardless.
        public DateTime? MatchedAtItemDate { get; set; }
    }
}
