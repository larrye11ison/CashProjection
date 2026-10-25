using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // Read-only display row joining a Presumptive Match with the
    // ProjectionItem and BankTransaction it links.
    public sealed class PresumptiveMatchRow
    {
        public Match Match { get; init; } = null!;
        public ProjectionItem ProjectionItem { get; init; } = null!;
        public BankTransaction BankTransaction { get; init; } = null!;

        // Same sign convention as BankTransaction.Amount (positive = money
        // out, negative = money in) so the two amounts line up directly
        // comparable in the review UI.
        public decimal ProjectionAmount => ProjectionItem.Payment ?? -(ProjectionItem.Deposit ?? 0m);

        // Pre-formatted for the dialog — WinUI's Binding markup has no
        // StringFormat. Labeled "Deposit"/"Payment" for the same reason as
        // PresumptiveMatchGroup.ProjectionAmountDisplay — Plaid's sign
        // convention (positive = money out, negative = money in) isn't
        // obvious from a bare amount.
        public string BankDateDisplay => BankTransaction.TxnDate.ToString("d");
        public string BankAmountDisplay => BankTransaction.Amount < 0
            ? $"Deposit {-BankTransaction.Amount:C2}"
            : $"Payment {BankTransaction.Amount:C2}";
        public string ScoreDisplay => $"Score: {Match.Score}";
    }
}
