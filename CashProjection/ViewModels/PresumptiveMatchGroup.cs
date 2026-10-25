using System.Collections.ObjectModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // One projection item (the parent) with every still-open presumptive
    // candidate bank transaction (the children) grouped beneath it, closest
    // candidate — by date, then amount — first.
    public sealed class PresumptiveMatchGroup
    {
        public ProjectionItem ProjectionItem { get; init; } = null!;

        public ObservableCollection<PresumptiveMatchRow> Candidates { get; } = new();

        public decimal ProjectionAmount => ProjectionItem.Payment ?? -(ProjectionItem.Deposit ?? 0m);

        // Pre-formatted for the dialog — WinUI's Binding markup has no
        // StringFormat. Labeled "Deposit"/"Payment" rather than just a
        // signed amount — a bare "$123.45" (or "($123.45)" for a negative)
        // doesn't make which-direction-is-this obvious at a glance the way
        // the main grid's separate Deposit/Payment columns do.
        public string ProjectionAmountDisplay =>
            ProjectionItem.Deposit is { } deposit ? $"Deposit {deposit:C2}" :
            ProjectionItem.Payment is { } payment ? $"Payment {payment:C2}" :
            "$0.00";
        public string ProjectionDateDisplay => ProjectionItem.TransactionDate.ToString("d");

        // Same rule as BankTransaction's vendor-name/raw-description pair —
        // only show the match pattern alongside the item's name when it's
        // actually set and differs from it; otherwise it's redundant.
        public bool HasDistinctMatchPattern =>
            !string.IsNullOrWhiteSpace(ProjectionItem.MatchText)
            && !string.Equals(ProjectionItem.MatchText.Trim(), ProjectionItem.Name.Trim(), StringComparison.Ordinal);
    }
}
