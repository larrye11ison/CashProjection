namespace CashProjection.Models
{
    // One bank account. The data model supports multiple rows, but the UI
    // (for now) always operates against the single row where IsActive = true.
    public sealed class Account
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? PlaidItemId { get; set; }
        public string? PlaidAccountId { get; set; }
        public decimal StartingBalance { get; set; }
        public DateTime? StartingBalanceAsOf { get; set; }

        // Separate from IsActive (which still just means "the account
        // AccountRepository.GetOrCreateActiveAccount() returns"). Only the
        // checking account carries recurring bills worth projecting/
        // matching — the other accounts linked under the same Plaid Item
        // (money market, savings, a CD, a line of credit, ...) are
        // balance-and-history-only in the UI.
        public bool SupportsProjections { get; set; }
    }
}
