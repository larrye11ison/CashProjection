namespace CashProjection.Services
{
    // Tracks, per account, how many newly-downloaded transactions the user
    // hasn't looked at yet. Deliberately in-memory only (not persisted) —
    // "unseen" is a UI-session concept: closing and reopening the app is
    // itself a reasonable point to forget it, and there's no need for a
    // migration just to remember a transient badge.
    public sealed class UnseenTransactionsTracker
    {
        private readonly Dictionary<int, int> _unseenCounts = new();

        public void RegisterNewTransactions(int accountId, int count)
        {
            if (count <= 0)
                return;

            _unseenCounts[accountId] = _unseenCounts.GetValueOrDefault(accountId) + count;
        }

        public int GetUnseenCount(int accountId) => _unseenCounts.GetValueOrDefault(accountId);

        public void MarkSeen(int accountId) => _unseenCounts.Remove(accountId);
    }
}
