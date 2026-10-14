namespace CashProjection.Models
{
    // A real transaction downloaded from Plaid. Read-only from the UI's
    // perspective — the app never writes these back to the bank, it only
    // mirrors what Plaid reports.
    public sealed class BankTransaction
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public string PlaidTransactionId { get; set; } = string.Empty;

        // Plaid's own account_id for whichever of the Item's accounts this
        // transaction actually posted to. A single Item/access_token can be
        // authorized for several accounts at once (checking, savings, a CD,
        // etc. all under one credit-union login) — without this, syncing
        // by Item alone pulls every authorized account's activity into one
        // undifferentiated pile, including both legs of internal transfers
        // between the user's own accounts. PlaidSyncService filters on this
        // so only the one account this Account row represents ever lands
        // here.
        public string? PlaidAccountId { get; set; }
        public DateTime TxnDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool Pending { get; set; }
        public string? Category { get; set; }

        // The balance after this transaction, as reported by the bank via
        // Plaid's running_balance field. Only populated for posted
        // transactions, and not every institution provides it — null means
        // "unknown," not zero.
        public decimal? RunningBalance { get; set; }
        public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
