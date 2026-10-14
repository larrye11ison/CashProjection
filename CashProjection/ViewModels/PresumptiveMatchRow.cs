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
    }
}
