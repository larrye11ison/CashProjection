using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class AccountRepository
    {
        private readonly CashProjectionDbContext _db;

        public AccountRepository(CashProjectionDbContext db) => _db = db;

        public Account GetOrCreateActiveAccount()
        {
            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf
                    FROM Accounts
                    WHERE IsActive = 1
                    LIMIT 1;
                    """;
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                    return Read(reader);
            }

            var account = new Account { Name = "My Account", IsActive = true, StartingBalance = 0m };
            Insert(account);
            return account;
        }

        public Account? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf
                FROM Accounts WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        // Used by PlaidSyncService's account-discovery pass to tell whether
        // a Plaid-reported account already has a local row.
        public Account? GetByPlaidAccountId(string plaidAccountId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf
                FROM Accounts WHERE PlaidAccountId = $pid LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$pid", plaidAccountId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        // Every account linked under the Plaid Item — used by the account
        // switcher. The default-selected (IsActive) account sorts first.
        public List<Account> GetAll()
        {
            var results = new List<Account>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf
                FROM Accounts
                ORDER BY IsActive DESC, Name ASC;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        public void Insert(Account account)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Accounts (Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf)
                VALUES ($name, $isActive, $plaidItemId, $plaidAccountId, $startingBalance, $startingBalanceAsOf);
                SELECT last_insert_rowid();
                """;
            BindCommon(cmd, account);
            account.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void Update(Account account)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Accounts
                SET Name = $name,
                    IsActive = $isActive,
                    PlaidItemId = $plaidItemId,
                    PlaidAccountId = $plaidAccountId,
                    StartingBalance = $startingBalance,
                    StartingBalanceAsOf = $startingBalanceAsOf
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", account.Id);
            BindCommon(cmd, account);
            cmd.ExecuteNonQuery();
        }

        private static void BindCommon(SqliteCommand cmd, Account account)
        {
            cmd.Parameters.AddWithValue("$name", account.Name);
            cmd.Parameters.AddWithValue("$isActive", account.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$plaidItemId", (object?)account.PlaidItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$plaidAccountId", (object?)account.PlaidAccountId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$startingBalance", account.StartingBalance.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue(
                "$startingBalanceAsOf",
                account.StartingBalanceAsOf.HasValue ? account.StartingBalanceAsOf.Value.ToString("O") : (object)DBNull.Value);
        }

        private static Account Read(SqliteDataReader reader) => new Account
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            IsActive = reader.GetInt32(2) != 0,
            PlaidItemId = reader.IsDBNull(3) ? null : reader.GetString(3),
            PlaidAccountId = reader.IsDBNull(4) ? null : reader.GetString(4),
            StartingBalance = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            StartingBalanceAsOf = reader.IsDBNull(6)
                ? null
                : DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
        };
    }
}
