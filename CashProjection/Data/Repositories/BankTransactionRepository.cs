using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class BankTransactionRepository
    {
        private readonly CashProjectionDbContext _db;

        public BankTransactionRepository(CashProjectionDbContext db) => _db = db;

        public List<BankTransaction> GetByAccount(int accountId)
        {
            var results = new List<BankTransaction>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId
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

        public BankTransaction? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId
                FROM BankTransactions WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public void Upsert(BankTransaction txn)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BankTransactions
                    (AccountId, PlaidTransactionId, TxnDate, Description, Amount, Pending, Category, ImportedAtUtc, RunningBalance, PlaidAccountId)
                VALUES
                    ($accountId, $plaidTransactionId, $txnDate, $description, $amount, $pending, $category, $importedAtUtc, $runningBalance, $plaidAccountId)
                ON CONFLICT(PlaidTransactionId) DO UPDATE SET
                    TxnDate = excluded.TxnDate,
                    Description = excluded.Description,
                    Amount = excluded.Amount,
                    Pending = excluded.Pending,
                    Category = excluded.Category,
                    RunningBalance = excluded.RunningBalance,
                    PlaidAccountId = excluded.PlaidAccountId;
                """;
            cmd.Parameters.AddWithValue("$accountId", txn.AccountId);
            cmd.Parameters.AddWithValue("$plaidTransactionId", txn.PlaidTransactionId);
            cmd.Parameters.AddWithValue("$txnDate", txn.TxnDate.ToString("O"));
            cmd.Parameters.AddWithValue("$description", txn.Description);
            cmd.Parameters.AddWithValue("$amount", txn.Amount.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$pending", txn.Pending ? 1 : 0);
            cmd.Parameters.AddWithValue("$category", (object?)txn.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$importedAtUtc", txn.ImportedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$runningBalance",
                txn.RunningBalance.HasValue ? txn.RunningBalance.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$plaidAccountId", (object?)txn.PlaidAccountId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
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
        };
    }
}
