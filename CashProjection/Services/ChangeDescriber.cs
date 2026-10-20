using CashProjection.Models;

namespace CashProjection.Services
{
    // Builds human-readable field-level diff text for change-history
    // descriptions, so the history view shows WHAT changed, not just that
    // "something" changed.
    public static class ChangeDescriber
    {
        public static string DescribeProjectionItemDiff(ProjectionItem before, ProjectionItem after)
        {
            var parts = new List<string>();

            if (before.Name != after.Name)
                parts.Add($"Name: '{before.Name}' → '{after.Name}'");
            if (before.TransactionDate != after.TransactionDate)
                parts.Add($"Date: {before.TransactionDate:d} → {after.TransactionDate:d}");
            if (before.Deposit != after.Deposit)
                parts.Add($"Deposit: {FormatAmount(before.Deposit)} → {FormatAmount(after.Deposit)}");
            if (before.Payment != after.Payment)
                parts.Add($"Payment: {FormatAmount(before.Payment)} → {FormatAmount(after.Payment)}");
            if (before.Periodicity != after.Periodicity)
                parts.Add($"Periodicity: {before.Periodicity} → {after.Periodicity}");
            if (before.MatchText != after.MatchText)
                parts.Add($"Match pattern: {FormatText(before.MatchText)} → {FormatText(after.MatchText)}");

            return parts.Count > 0 ? string.Join("; ", parts) : "No field changes detected";
        }

        private static string FormatAmount(decimal? value) => value.HasValue ? value.Value.ToString("C2") : "(none)";

        private static string FormatText(string? value) => string.IsNullOrEmpty(value) ? "(none)" : $"'{value}'";
    }
}
