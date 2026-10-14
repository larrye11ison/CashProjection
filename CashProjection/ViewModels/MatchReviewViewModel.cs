using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Data.Repositories;
using CashProjection.Models;
using CashProjection.Services;

namespace CashProjection.ViewModels
{
    // Lists presumptive matches found by FuzzyMatchingService and lets the
    // user Accept (confirm + reschedule via ProjectionRescheduler, carrying
    // the real amount forward) or Reject (flag the projection item for
    // review, and remember the rejection so it's never re-suggested).
    public sealed partial class MatchReviewViewModel : ObservableObject
    {
        private readonly MatchRepository _matchRepo;
        private readonly ProjectionItemRepository _itemRepo;
        private readonly BankTransactionRepository _bankRepo;
        private readonly ChangeHistoryService _changeHistory;
        private readonly Action _onDataChanged;

        public ObservableCollection<PresumptiveMatchRow> PresumptiveMatches { get; } = new();

        public MatchReviewViewModel(
            MatchRepository matchRepo,
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            ChangeHistoryService changeHistory,
            Action onDataChanged)
        {
            _matchRepo = matchRepo;
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _changeHistory = changeHistory;
            _onDataChanged = onDataChanged;
            Reload();
        }

        public void Reload()
        {
            PresumptiveMatches.Clear();
            foreach (var match in _matchRepo.GetByStatus(MatchStatus.Presumptive))
            {
                var item = _itemRepo.GetById(match.ProjectionItemId);
                var txn = _bankRepo.GetById(match.BankTransactionId);
                if (item is null || txn is null)
                    continue;

                PresumptiveMatches.Add(new PresumptiveMatchRow
                {
                    Match = match,
                    ProjectionItem = item,
                    BankTransaction = txn,
                });
            }
        }

        [RelayCommand]
        public void Accept(PresumptiveMatchRow? row)
        {
            if (row is null)
                return;

            // Accepting is one logical action even though it touches two
            // entities (the Match's status and the item's reschedule) —
            // share a batch id so Undo/Redo always treat them as a unit and
            // can never leave the match confirmed but the item un-rescheduled
            // (or vice versa).
            var batchId = _changeHistory.NewBatchId();

            var beforeMatch = Clone(row.Match);
            var afterMatch = Clone(row.Match);
            afterMatch.Status = MatchStatus.Confirmed;
            afterMatch.DecidedAtUtc = DateTime.UtcNow;
            // Snapshot the occurrence being matched now, before rescheduling
            // moves the live item on to its next (different) occurrence —
            // this is what lets the grid know to stop pairing the live item
            // with this transaction once it's no longer the same occurrence,
            // while the transaction itself stays permanently "crossed off."
            afterMatch.MatchedAtItemDate = row.ProjectionItem.TransactionDate;
            _matchRepo.Update(afterMatch);
            _changeHistory.Record(
                ChangeEntityType.Match, row.Match.Id, ChangeType.MatchAccepted,
                beforeMatch, afterMatch,
                $"Matched '{row.ProjectionItem.Name}' to '{row.BankTransaction.Description}'",
                batchId);

            // Reschedule using the real matched amount as the new baseline estimate.
            var beforeItem = row.ProjectionItem;
            var allItems = _itemRepo.GetByAccount(row.ProjectionItem.AccountId).Select(i => new ProjectionItemViewModel(i)).ToList();
            var itemVm = allItems.First(i => i.Id == row.ProjectionItem.Id);
            ProjectionRescheduler.Advance(itemVm, allItems, realizedAmount: row.BankTransaction.Amount);
            var afterItem = itemVm.ToModel();
            _itemRepo.Update(afterItem);
            _changeHistory.Record(
                ChangeEntityType.ProjectionItem, afterItem.Id, ChangeType.Rescheduled,
                beforeItem, afterItem,
                $"Rescheduled '{afterItem.Name}' after accepting match — {ChangeDescriber.DescribeProjectionItemDiff(beforeItem, afterItem)}",
                batchId);

            PresumptiveMatches.Remove(row);

            // The item and the transaction are both spoken for now — any
            // other still-open suggestion involving either one (e.g. the
            // same item against a different, wrong transaction) is stale
            // and must be superseded, or it would keep sitting in the
            // review queue even though this occurrence is already decided.
            foreach (var stale in _matchRepo.GetOtherPresumptiveMatches(row.Match.Id, row.ProjectionItem.Id, row.BankTransaction.Id))
            {
                var beforeStale = Clone(stale);
                var afterStale = Clone(stale);
                afterStale.Status = MatchStatus.Rejected;
                afterStale.DecidedAtUtc = DateTime.UtcNow;
                _matchRepo.Update(afterStale);
                _changeHistory.Record(
                    ChangeEntityType.Match, stale.Id, ChangeType.MatchRejected,
                    beforeStale, afterStale,
                    $"Superseded by confirming a different match for '{row.ProjectionItem.Name}' / '{row.BankTransaction.Description}'",
                    batchId);

                var staleRow = PresumptiveMatches.FirstOrDefault(r => r.Match.Id == stale.Id);
                if (staleRow is not null)
                    PresumptiveMatches.Remove(staleRow);
            }

            _onDataChanged();
        }

        [RelayCommand]
        public void Reject(PresumptiveMatchRow? row)
        {
            if (row is null)
                return;

            var beforeMatch = Clone(row.Match);
            var afterMatch = Clone(row.Match);
            afterMatch.Status = MatchStatus.Rejected;
            afterMatch.DecidedAtUtc = DateTime.UtcNow;
            _matchRepo.Update(afterMatch);
            _changeHistory.Record(
                ChangeEntityType.Match, row.Match.Id, ChangeType.MatchRejected,
                beforeMatch, afterMatch,
                $"Rejected presumptive match for '{row.ProjectionItem.Name}' against '{row.BankTransaction.Description}'");

            var item = row.ProjectionItem;
            item.NeedsReview = true;
            _itemRepo.Update(item);

            PresumptiveMatches.Remove(row);
            _onDataChanged();
        }

        private static Match Clone(Match m) => new Match
        {
            Id = m.Id,
            ProjectionItemId = m.ProjectionItemId,
            BankTransactionId = m.BankTransactionId,
            Status = m.Status,
            Score = m.Score,
            CreatedAtUtc = m.CreatedAtUtc,
            DecidedAtUtc = m.DecidedAtUtc,
            MatchedAtItemDate = m.MatchedAtItemDate,
        };
    }
}
