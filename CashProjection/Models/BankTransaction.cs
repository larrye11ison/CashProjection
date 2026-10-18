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

        // Plaid's own cleaned-up/enriched merchant name (merchant_name,
        // falling back to the legacy name field) — may omit real detail the
        // bank's own text carries (e.g. "AT&T" instead of "BILL PAY AT&T
        // Mobility"). Kept separate from Description specifically so the UI
        // can show both when they diverge, and so matching can deliberately
        // ignore this field and use only the raw bank text.
        public string? VendorName { get; set; }

        // The bank's own raw transaction text (Plaid's original_description
        // when available, falling back to VendorName). This is what
        // FuzzyMatchingService matches against — never VendorName.
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

        // Single canonical "how should this transaction's name/description
        // be shown" rule, so every UI surface that lists a BankTransaction
        // (the checking grid, the other-accounts grid, presumptive match
        // review) agrees on it rather than each re-deriving its own copy.
        public string PrimaryDisplayName => string.IsNullOrWhiteSpace(VendorName) ? Description : VendorName;

        public bool HasDistinctVendorName =>
            !string.IsNullOrWhiteSpace(VendorName)
            && !string.Equals(VendorName.Trim(), Description.Trim(), StringComparison.Ordinal);
    }
}
