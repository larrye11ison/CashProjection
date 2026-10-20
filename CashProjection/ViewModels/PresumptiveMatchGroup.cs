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

        // Same rule as BankTransaction's vendor-name/raw-description pair —
        // only show the match pattern alongside the item's name when it's
        // actually set and differs from it; otherwise it's redundant.
        public bool HasDistinctMatchPattern =>
            !string.IsNullOrWhiteSpace(ProjectionItem.MatchText)
            && !string.Equals(ProjectionItem.MatchText.Trim(), ProjectionItem.Name.Trim(), StringComparison.Ordinal);
    }
}
