using System.Linq;
using System.Text.Json;
using CashProjection.Data.Repositories;
using CashProjection.Models;

namespace CashProjection.Services
{
    // Single call-in point for recording edits, and the engine behind
    // Undo/RollbackTo. Operates purely on repositories/DB rows — callers
    // (ViewModels) are responsible for refreshing their own in-memory
    // collections afterward.
    public sealed class ChangeHistoryService
    {
        private readonly ChangeHistoryRepository _historyRepo;
        private readonly ProjectionItemRepository _itemRepo;
        private readonly MatchRepository _matchRepo;

        public ChangeHistoryService(
            ChangeHistoryRepository historyRepo,
            ProjectionItemRepository itemRepo,
            MatchRepository matchRepo)
        {
            _historyRepo = historyRepo;
            _itemRepo = itemRepo;
            _matchRepo = matchRepo;
        }

        public void Record(ChangeEntityType entityType, int entityId, ChangeType changeType, object? before, object? after, string description, string? batchId = null)
        {
            _historyRepo.Insert(new ChangeHistoryEntry
            {
                EntityType = entityType,
                EntityId = entityId,
                ChangeType = changeType,
                BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
                AfterJson = after is null ? null : JsonSerializer.Serialize(after),
                Description = description,
                TimestampUtc = DateTime.UtcNow,
                BatchId = batchId,
            });
        }

        // Call once per logical user action that should Record(...) more
        // than once (e.g. accepting a match = a Match status change + a
        // projection item reschedule), and pass the result to every Record
        // call for that action — they'll always be undone/redone together.
        public string NewBatchId() => Guid.NewGuid().ToString("N");

        public List<ChangeHistoryEntry> GetAllNewestFirst() => _historyRepo.GetAllNewestFirst();

        public void Undo(int entryId)
        {
            var entry = _historyRepo.GetById(entryId)
                ?? throw new InvalidOperationException($"History entry {entryId} not found.");

            if (entry.BatchId is not null)
            {
                // Reverse newest-first within the batch.
                foreach (var batchEntry in _historyRepo.GetByBatchId(entry.BatchId).AsEnumerable().Reverse())
                    ApplyReverse(batchEntry);
            }
            else
            {
                ApplyReverse(entry);
            }
        }

        // Re-applies a previously-undone entry's AfterJson — the forward
        // direction, mirroring Undo's reverse direction.
        public void Redo(int entryId)
        {
            var entry = _historyRepo.GetById(entryId)
                ?? throw new InvalidOperationException($"History entry {entryId} not found.");

            if (entry.BatchId is not null)
            {
                // Re-apply oldest-first within the batch.
                foreach (var batchEntry in _historyRepo.GetByBatchId(entry.BatchId))
                    ApplyForward(batchEntry);
            }
            else
            {
                ApplyForward(entry);
            }
        }

        // Reverse-applies every entry newer than the given point in time,
        // newest first, so the data ends up exactly as it was at that moment.
        // If the cutoff falls in the middle of a batch, the whole batch is
        // still reversed together — otherwise a match's Confirmed status and
        // its reschedule could end up split across the boundary.
        public void RollbackTo(DateTime timestampUtc)
        {
            var entries = _historyRepo.GetNewerThan(timestampUtc);

            var expanded = new List<ChangeHistoryEntry>(entries);
            var seenIds = entries.Select(e => e.Id).ToHashSet();
            var batchIds = entries.Where(e => e.BatchId is not null).Select(e => e.BatchId!).Distinct();
            foreach (var batchId in batchIds)
            {
                foreach (var batchEntry in _historyRepo.GetByBatchId(batchId))
                {
                    if (seenIds.Add(batchEntry.Id))
                        expanded.Add(batchEntry);
                }
            }

            foreach (var entry in expanded.OrderByDescending(e => e.Id))
                ApplyReverse(entry);
        }

        private void ApplyReverse(ChangeHistoryEntry entry)
        {
            switch (entry.EntityType)
            {
                case ChangeEntityType.ProjectionItem:
                    ApplyProjectionItemReverse(entry);
                    break;
                case ChangeEntityType.Match:
                    ApplyMatchReverse(entry);
                    break;
            }
        }

        private void ApplyProjectionItemReverse(ChangeHistoryEntry entry)
        {
            switch (entry.ChangeType)
            {
                case ChangeType.Added:
                    _itemRepo.Delete(entry.EntityId);
                    break;

                case ChangeType.Deleted:
                    if (entry.BeforeJson is not null)
                        _itemRepo.InsertWithId(JsonSerializer.Deserialize<ProjectionItem>(entry.BeforeJson)!);
                    break;

                case ChangeType.Modified:
                case ChangeType.Rescheduled:
                    if (entry.BeforeJson is not null)
                        _itemRepo.Update(JsonSerializer.Deserialize<ProjectionItem>(entry.BeforeJson)!);
                    break;
            }
        }

        private void ApplyMatchReverse(ChangeHistoryEntry entry)
        {
            switch (entry.ChangeType)
            {
                case ChangeType.MatchAccepted:
                case ChangeType.MatchRejected:
                    if (entry.BeforeJson is not null)
                        _matchRepo.Update(JsonSerializer.Deserialize<Match>(entry.BeforeJson)!);
                    break;
            }
        }

        private void ApplyForward(ChangeHistoryEntry entry)
        {
            switch (entry.EntityType)
            {
                case ChangeEntityType.ProjectionItem:
                    ApplyProjectionItemForward(entry);
                    break;
                case ChangeEntityType.Match:
                    ApplyMatchForward(entry);
                    break;
            }
        }

        private void ApplyProjectionItemForward(ChangeHistoryEntry entry)
        {
            switch (entry.ChangeType)
            {
                case ChangeType.Added:
                    if (entry.AfterJson is not null)
                        _itemRepo.InsertWithId(JsonSerializer.Deserialize<ProjectionItem>(entry.AfterJson)!);
                    break;

                case ChangeType.Deleted:
                    _itemRepo.Delete(entry.EntityId);
                    break;

                case ChangeType.Modified:
                case ChangeType.Rescheduled:
                    if (entry.AfterJson is not null)
                        _itemRepo.Update(JsonSerializer.Deserialize<ProjectionItem>(entry.AfterJson)!);
                    break;
            }
        }

        private void ApplyMatchForward(ChangeHistoryEntry entry)
        {
            switch (entry.ChangeType)
            {
                case ChangeType.MatchAccepted:
                case ChangeType.MatchRejected:
                    if (entry.AfterJson is not null)
                        _matchRepo.Update(JsonSerializer.Deserialize<Match>(entry.AfterJson)!);
                    break;
            }
        }
    }
}
