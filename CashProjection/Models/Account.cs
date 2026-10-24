namespace CashProjection.Models
{
    // One bank account. Every linked account gets the same projection/
    // matching-capable view now — IsActive just means "the account shown
    // by default when the app launches," nothing more.
    public sealed class Account
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? PlaidItemId { get; set; }
        public string? PlaidAccountId { get; set; }
        public decimal StartingBalance { get; set; }
        public DateTime? StartingBalanceAsOf { get; set; }
    }
}
