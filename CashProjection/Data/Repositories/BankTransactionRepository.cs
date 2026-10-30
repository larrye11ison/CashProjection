using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class BankTransactionRepository
    {
        private readonly CashProjectionDbContext _db;

        public BankTransactionRepository(CashProjectionDbContext db) => _db = db;

        // TxnDate is always stored as a Kind=Unspecified "O" string (it
        // comes from Plaid's date-only field via DateOnly.ToDateTime,
        // which never carries an offset) — every comparison against it
        // needs to go through this, not a bare .ToString("O"), or a
        // caller-supplied DateTime.Today/DateTime.Now (Kind=Local) would
        // serialize with a "-07:00"-style suffix the stored strings never
        // have, silently breaking the lexicographic comparison SQLite
        // does on TEXT dates for rows that land on the exact boundary day.
        private static string ToTxnDateParam(DateTime date) =>
            DateTime.SpecifyKind(date, DateTimeKind.Unspecified).ToString("O");

        public List<BankTransaction> GetByAccount(int accountId)
        {
            var results = new List<BankTransaction>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions
                WHERE AccountId = $accountId
                ORDER BY TxnDate DESC, Id DESC;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            return results;
        }

        // Windowed initial load — date-bounded rather than count-bounded so
        // sparse/dense months don't need special-casing (AccountProjectionViewModel's
        // initial 3-month window).
        public List<BankTransaction> GetSince(int accountId, DateTime sinceDate)
        {
            var results = new List<BankTransaction>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions
                WHERE AccountId = $accountId AND TxnDate >= $sinceDate
                ORDER BY TxnDate DESC, Id DESC;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$sinceDate", ToTxnDateParam(sinceDate));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            return results;
        }

        // Keyset pagination extending an already-loaded range in either
        // direction from its edge cursor — used both for ordinary
        // scroll-triggered "load more" (older = true, extending toward the
        // past) and for extending a Find-jumped window back toward "now"
        // (older = false). Results always come back in the app's usual
        // TxnDate DESC, Id DESC order regardless of direction queried.
        public List<BankTransaction> GetPage(int accountId, DateTime cursorDate, int cursorId, int pageSize, bool older)
        {
            var results = new List<BankTransaction>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = older
                ? """
                    SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                    FROM BankTransactions
                    WHERE AccountId = $accountId
                      AND (TxnDate < $cursorDate OR (TxnDate = $cursorDate AND Id < $cursorId))
                    ORDER BY TxnDate DESC, Id DESC
                    LIMIT $pageSize;
                    """
                : """
                    SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                    FROM BankTransactions
                    WHERE AccountId = $accountId
                      AND (TxnDate > $cursorDate OR (TxnDate = $cursorDate AND Id > $cursorId))
                    ORDER BY TxnDate ASC, Id ASC
                    LIMIT $pageSize;
                    """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$cursorDate", ToTxnDateParam(cursorDate));
            cmd.Parameters.AddWithValue("$cursorId", cursorId);
            cmd.Parameters.AddWithValue("$pageSize", pageSize);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            // The "newer" query above has to run ASC to LIMIT to the rows
            // nearest the cursor — flip back to the app's usual DESC order
            // before returning.
            if (!older)
                results.Reverse();

            return results;
        }

        // All transactions within +/- windowDays of centerDate — used to
        // open a new loaded range around a Find result that landed outside
        // every range currently loaded in the grid.
        public List<BankTransaction> GetAroundDate(int accountId, DateTime centerDate, int windowDays = 30) =>
            GetBetween(accountId, centerDate.AddDays(-windowDays), centerDate.AddDays(windowDays));

        // All transactions within an explicit inclusive date range — used
        // to re-fetch a previously-loaded (non-main) range's exact span
        // after Undo/Redo/CorrectBalance, so the loaded window survives a
        // reload instead of collapsing back to the initial window.
        public List<BankTransaction> GetBetween(int accountId, DateTime startDate, DateTime endDate)
        {
            var results = new List<BankTransaction>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions
                WHERE AccountId = $accountId AND TxnDate >= $start AND TxnDate <= $end
                ORDER BY TxnDate DESC, Id DESC;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$start", ToTxnDateParam(startDate));
            cmd.Parameters.AddWithValue("$end", ToTxnDateParam(endDate));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            return results;
        }

        // Cheap existence checks used to set/clear a loaded range's
        // boundary "might be more" flags without fetching a whole page —
        // and to resolve a flag to "no more" once a direction is confirmed
        // exhausted.
        public bool HasMoreBefore(int accountId, DateTime date, int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM BankTransactions
                WHERE AccountId = $accountId AND (TxnDate < $date OR (TxnDate = $date AND Id < $id))
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$date", ToTxnDateParam(date));
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteScalar() is not null;
        }

        public bool HasMoreAfter(int accountId, DateTime date, int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM BankTransactions
                WHERE AccountId = $accountId AND (TxnDate > $date OR (TxnDate = $date AND Id > $id))
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$date", ToTxnDateParam(date));
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteScalar() is not null;
        }

        // Searches the account's full history by default (sinceDate is an
        // optional caller-imposed bound, unused today but left available).
        // Matches the existing in-memory Find semantics — substring match
        // against Description only, not VendorName — just evaluated in SQL
        // so it isn't limited to whatever's currently loaded in the grid.
        public List<BankTransaction> SearchByAccount(int accountId, string[] tokens, DateTime? sinceDate = null)
        {
            var results = new List<BankTransaction>();
            if (tokens.Length == 0)
                return results;

            using var cmd = _db.Connection.CreateCommand();
            var likeClauses = string.Join(" AND ", tokens.Select((_, i) => $"Description LIKE $tok{i}"));
            cmd.CommandText = $"""
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions
                WHERE AccountId = $accountId
                  AND ({likeClauses})
                  {(sinceDate.HasValue ? "AND TxnDate >= $sinceDate" : "")}
                ORDER BY TxnDate DESC, Id DESC;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            for (var i = 0; i < tokens.Length; i++)
                cmd.Parameters.AddWithValue($"$tok{i}", $"%{tokens[i]}%");
            if (sinceDate.HasValue)
                cmd.Parameters.AddWithValue("$sinceDate", ToTxnDateParam(sinceDate.Value));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            return results;
        }

        public BankTransaction? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public BankTransaction? GetByPlaidTransactionId(string plaidTransactionId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance
                FROM BankTransactions WHERE PlaidTransactionId = $plaidTransactionId;
                """;
            cmd.Parameters.AddWithValue("$plaidTransactionId", plaidTransactionId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public void Upsert(BankTransaction txn)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BankTransactions
                    (AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId, VendorName, EstimatedBalance)
                VALUES
                    ($accountId, $plaidTransactionId, $txnDate, $description, $amount, $pending, $category, $importedAtUtc, $runningBalance, $plaidAccountId, $vendorName, 0)
                ON CONFLICT(PlaidTransactionId) DO UPDATE SET
                    TxnDate = excluded.TxnDate,
                    Description = excluded.Description,
                    Amount = excluded.Amount,
                    Pending = excluded.Pending,
                    Category = excluded.Category,
                    RunningBalance = excluded.RunningBalance,
                    PlaidAccountId = excluded.PlaidAccountId,
                    VendorName = excluded.VendorName;
                """;
            cmd.Parameters.AddWithValue("$accountId", txn.AccountId);
            cmd.Parameters.AddWithValue("$plaidTransactionId", txn.PlaidTransactionId);
            cmd.Parameters.AddWithValue("$txnDate", ToTxnDateParam(txn.TxnDate));
            cmd.Parameters.AddWithValue("$description", txn.Description);
            cmd.Parameters.AddWithValue("$amount", txn.Amount.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$pending", txn.Pending ? 1 : 0);
            cmd.Parameters.AddWithValue("$category", (object?)txn.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$importedAtUtc", txn.ImportedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$runningBalance",
                txn.RunningBalance.HasValue ? txn.RunningBalance.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$plaidAccountId", (object?)txn.PlaidAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vendorName", (object?)txn.VendorName ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // Narrow update used by the "Correct Balance" popover — a
        // deliberate manual override of a specific transaction's
        // bank-reported (or previously estimated) running balance. Touches
        // only this one column; everything else about the row (amount,
        // description, etc.) stays exactly as Plaid reported it.
        public void UpdateRunningBalance(int id, decimal? runningBalance)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "UPDATE BankTransactions SET RunningBalance = $runningBalance WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue(
                "$runningBalance",
                runningBalance.HasValue ? runningBalance.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // Recomputes and persists EstimatedBalance for every real
        // transaction in this account's full history, so any window read
        // later (initial load, a scrolled-to page, or a Find-jumped window
        // far from "now") already carries a correct Balance with no need
        // to stitch it into one contiguous in-memory chain first. Mirrors
        // the checkpoint-aware backward walk in
        // AccountProjectionViewModel.ResortAndRecalculate, just over the
        // full account history instead of whatever's currently loaded in
        // the UI, and written back instead of held in memory. Cheap even
        // run in full every time at the realistic scale of a personal
        // checking account's lifetime history.
        public void RecalculateEstimatedBalances(int accountId, decimal currentLiveBalance)
        {
            var rows = new List<(int Id, DateTime TxnDate, decimal Amount, decimal? RunningBalance)>();

            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, TxnDate, Amount, RunningBalance
                    FROM BankTransactions
                    WHERE AccountId = $accountId
                    ORDER BY TxnDate DESC, Id DESC;
                    """;
                cmd.Parameters.AddWithValue("$accountId", accountId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((
                        reader.GetInt32(0),
                        DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
                        decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                        reader.IsDBNull(3) ? null : decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
                }
            }

            var estimatedBalanceById = new Dictionary<int, decimal>();
            var preCheckpointIds = new List<int>();
            decimal? pendingGapAmount = null;
            var runningReal = currentLiveBalance;

            foreach (var dateGroup in rows.GroupBy(r => r.TxnDate.Date).OrderByDescending(g => g.Key))
            {
                var checkpoint = dateGroup.FirstOrDefault(r => r.RunningBalance.HasValue);
                if (checkpoint.RunningBalance is { } knownBalance)
                {
                    pendingGapAmount ??= runningReal - knownBalance;
                    runningReal = knownBalance;
                }
                else if (pendingGapAmount is null)
                {
                    preCheckpointIds.AddRange(dateGroup.Select(r => r.Id));
                }

                foreach (var row in dateGroup)
                    estimatedBalanceById[row.Id] = runningReal;

                var netEffect = dateGroup.Sum(r => -r.Amount);
                runningReal -= netEffect;
            }

            // Every row walked before the first checkpoint was assigned
            // from the raw live balance with no idea yet that a gap (e.g.
            // Plaid sync latency) existed between it and that checkpoint —
            // correct them retroactively now that the gap is known, same
            // as ResortAndRecalculate does for the in-memory case.
            if (pendingGapAmount is { } gap && Math.Abs(gap) >= 0.01m)
            {
                foreach (var id in preCheckpointIds)
                    estimatedBalanceById[id] -= gap;
            }

            using var transaction = _db.Connection.BeginTransaction();
            foreach (var (id, estimatedBalance) in estimatedBalanceById)
            {
                using var cmd = _db.Connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE BankTransactions SET EstimatedBalance = $estimatedBalance WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$estimatedBalance", estimatedBalance.ToString(CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        public void Remove(string plaidTransactionId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "DELETE FROM BankTransactions WHERE PlaidTransactionId = $plaidTransactionId;";
            cmd.Parameters.AddWithValue("$plaidTransactionId", plaidTransactionId);
            cmd.ExecuteNonQuery();
        }

        private static BankTransaction Read(SqliteDataReader reader) => new BankTransaction
        {
            Id = reader.GetInt32(0),
            AccountId = reader.GetInt32(1),
            PlaidTransactionId = reader.GetString(2),
            TxnDate = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
            Description = reader.GetString(4),
            Amount = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            Pending = reader.GetInt32(6) != 0,
            Category = reader.IsDBNull(7) ? null : reader.GetString(7),
            ImportedAtUtc = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            RunningBalance = reader.IsDBNull(9) ? null : decimal.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
            PlaidAccountId = reader.IsDBNull(10) ? null : reader.GetString(10),
            VendorName = reader.IsDBNull(11) ? null : reader.GetString(11),
            EstimatedBalance = decimal.Parse(reader.GetString(12), CultureInfo.InvariantCulture),
        };
    }
}
