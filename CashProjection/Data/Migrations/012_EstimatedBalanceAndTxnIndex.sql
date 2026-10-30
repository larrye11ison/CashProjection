-- EstimatedBalance is the app's own computed running balance for a real
-- transaction (distinct from RunningBalance, which is only ever a known
-- fact — Plaid-reported or a manual correction). NOT NULL so every read
-- can trust it without a null check; the '0' default here is only a
-- placeholder for existing rows, immediately overwritten by the
-- one-time startup backfill (see CashProjectionDbContext/App startup)
-- that runs BankTransactionRepository.RecalculateEstimatedBalances for
-- every account right after this migration applies.
ALTER TABLE BankTransactions ADD COLUMN EstimatedBalance TEXT NOT NULL DEFAULT '0';

-- Supports both the existing GetByAccount ordering and the new
-- keyset-paginated windowed reads (GetSince/GetPage/GetAroundDate).
CREATE INDEX IF NOT EXISTS IX_BankTransactions_AccountId_TxnDate_Id
    ON BankTransactions(AccountId, TxnDate DESC, Id DESC);
