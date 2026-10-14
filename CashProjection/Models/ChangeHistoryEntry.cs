namespace CashProjection.Models
{
    public enum ChangeEntityType
    {
        ProjectionItem,
        Match,
    }

    public enum ChangeType
    {
        Added,
        Deleted,
        Modified,
        Rescheduled,
        MatchAccepted,
        MatchRejected,
    }

    // One row in the audit/undo log. BeforeJson/AfterJson are serialized
    // snapshots of the affected entity (ProjectionItem or Match), used to
    // reverse-apply the change for Undo/RollbackTo.
    public sealed class ChangeHistoryEntry
    {
        public int Id { get; set; }
        public ChangeEntityType EntityType { get; set; }
        public int EntityId { get; set; }
        public ChangeType ChangeType { get; set; }
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        // Entries sharing a BatchId were recorded as one logical user action
        // (e.g. accepting a match = a Match status change + a reschedule)
        // and are always undone/redone together, never independently.
        public string? BatchId { get; set; }

        // Display-only convenience for the History view — never persisted.
        public DateTime LocalTimestamp => TimestampUtc.ToLocalTime();
    }
}
