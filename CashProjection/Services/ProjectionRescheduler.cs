using CashProjection.Models;
using CashProjection.ViewModels;

namespace CashProjection.Services
{
    // The periodicity-based "push forward" logic, shared by the manual
    // PushForward button and by accepting a presumptive match (which also
    // carries the real matched amount forward as the item's new estimate).
    public static class ProjectionRescheduler
    {
        public static void Advance(ProjectionItemViewModel item, IEnumerable<ProjectionItemViewModel> allItems, decimal? realizedAmount = null)
        {
            if (item.Periodicity == Periodicity.TwoWeeksPastLast)
            {
                var lastTransaction = allItems
                    .Where(t => t.Name == item.Name && !ReferenceEquals(t, item))
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefault();

                // No same-named sibling to anchor to (e.g. this occurrence
                // was renamed independently of the rest of its series) —
                // fall back to advancing 14 days from itself rather than
                // falling through to the switch below, where
                // TwoWeeksPastLast isn't a handled case and the date would
                // silently stay put.
                var anchor = lastTransaction?.TransactionDate ?? item.TransactionDate;
                item.TransactionDate = anchor.AddDays(14);
                ApplyRealizedAmount(item, realizedAmount);
                return;
            }

            item.TransactionDate = item.Periodicity switch
            {
                Periodicity.Monthly => item.TransactionDate.AddMonths(1),
                Periodicity.Quarterly => item.TransactionDate.AddMonths(3),
                Periodicity.SemiAnnually => item.TransactionDate.AddMonths(6),
                Periodicity.Annually => item.TransactionDate.AddYears(1),
                _ => item.TransactionDate,
            };

            ApplyRealizedAmount(item, realizedAmount);
        }

        private static void ApplyRealizedAmount(ProjectionItemViewModel item, decimal? realizedAmount)
        {
            if (!realizedAmount.HasValue)
                return;

            // Carry the real matched amount forward as the new baseline
            // estimate, rather than leaving a stale manually-entered guess.
            if (item.Payment.HasValue)
                item.Payment = Math.Abs(realizedAmount.Value);
            else
                item.Deposit = Math.Abs(realizedAmount.Value);
        }
    }
}
