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
                    SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf, SupportsProjections
                    FROM Accounts
                    WHERE IsActive = 1
                    LIMIT 1;
                    """;
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                    return Read(reader);
            }

            var account = new Account { Name = "My Account", IsActive = true, SupportsProjections = true, StartingBalance = 0m };
            Insert(account);
            return account;
        }

        // Every account linked under the Plaid Item, checking first — used
        // by the account switcher. Checking (SupportsProjections = true)
        // sorts first so it's always the default selection.
        public List<Account> GetAll()
        {
            var results = new List<Account>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf, SupportsProjections
                FROM Accounts
                ORDER BY SupportsProjections DESC, Name ASC;
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
                INSERT INTO Accounts (Name, IsActive, PlaidItemId, PlaidAccountId, StartingBalance, StartingBalanceAsOf, SupportsProjections)
                VALUES ($name, $isActive, $plaidItemId, $plaidAccountId, $startingBalance, $startingBalanceAsOf, $supportsProjections);
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
                    StartingBalanceAsOf = $startingBalanceAsOf,
                    SupportsProjections = $supportsProjections
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
            cmd.Parameters.AddWithValue("$supportsProjections", account.SupportsProjections ? 1 : 0);
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
            SupportsProjections = reader.GetInt32(7) != 0,
        };
    }
}
