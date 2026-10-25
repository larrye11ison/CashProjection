using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class ProjectionItemRepository
    {
        private readonly CashProjectionDbContext _db;

        public ProjectionItemRepository(CashProjectionDbContext db) => _db = db;

        public List<ProjectionItem> GetByAccount(int accountId)
        {
            var results = new List<ProjectionItem>();

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, Name, ItemDate, Deposit, Payment, Periodicity, MatchText, NeedsReview, UpdatedAtUtc
                FROM ProjectionItems
                WHERE AccountId = $accountId
                ORDER BY Id;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));

            return results;
        }

        // Autoincrement insert — sets item.Id from the new row.
        public void Insert(ProjectionItem item)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProjectionItems
                    (AccountId, Name, ItemDate, Deposit, Payment, Periodicity, MatchText, NeedsReview, UpdatedAtUtc)
                VALUES
                    ($accountId, $name, $itemDate, $deposit, $payment, $periodicity, $matchText, $needsReview, $updatedAtUtc);
                SELECT last_insert_rowid();
                """;
            BindCommon(cmd, item);
            item.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Explicit-Id insert — used only to restore a row previously removed
        // by DeleteTransaction, as part of reversing a "Deleted" history entry.
        public void InsertWithId(ProjectionItem item)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProjectionItems
                    (Id, AccountId, Name, ItemDate, Deposit, Payment, Periodicity, MatchText, NeedsReview, UpdatedAtUtc)
                VALUES
                    ($id, $accountId, $name, $itemDate, $deposit, $payment, $periodicity, $matchText, $needsReview, $updatedAtUtc);
                """;
            cmd.Parameters.AddWithValue("$id", item.Id);
            BindCommon(cmd, item);
            cmd.ExecuteNonQuery();
        }

        public void Update(ProjectionItem item)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE ProjectionItems
                SET Name = $name,
                    ItemDate = $itemDate,
                    Deposit = $deposit,
                    Payment = $payment,
                    Periodicity = $periodicity,
                    MatchText = $matchText,
                    NeedsReview = $needsReview,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", item.Id);
            BindCommon(cmd, item);
            cmd.ExecuteNonQuery();
        }

        // Matches.ProjectionItemId REFERENCES ProjectionItems(Id) with FK
        // enforcement on (CashProjectionDbContext sets PRAGMA foreign_keys
        // = ON) and no ON DELETE CASCADE — deleting an item that has ANY
        // Match row against it (presumptive, confirmed, or rejected; the
        // fuzzy-matching engine creates these automatically, so most items
        // have at least one) fails with "FOREIGN KEY constraint failed"
        // unless those rows go first. Known tradeoff: undoing this delete
        // (ChangeHistoryService restores the projection item itself) does
        // NOT restore whatever match relationships it had — only the item
        // is snapshotted for Undo, not its Matches rows.
        public void Delete(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                DELETE FROM Matches WHERE ProjectionItemId = $id;
                DELETE FROM ProjectionItems WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public ProjectionItem? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, Name, ItemDate, Deposit, Payment, Periodicity, MatchText, NeedsReview, UpdatedAtUtc
                FROM ProjectionItems WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        private static void BindCommon(SqliteCommand cmd, ProjectionItem item)
        {
            cmd.Parameters.AddWithValue("$accountId", item.AccountId);
            cmd.Parameters.AddWithValue("$name", item.Name);
            cmd.Parameters.AddWithValue("$itemDate", item.TransactionDate.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$deposit",
                item.Deposit.HasValue ? item.Deposit.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "$payment",
                item.Payment.HasValue ? item.Payment.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$periodicity", (int)item.Periodicity);
            cmd.Parameters.AddWithValue("$matchText", (object?)item.MatchText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$needsReview", item.NeedsReview ? 1 : 0);
            cmd.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        }

        private static ProjectionItem Read(SqliteDataReader reader) => new ProjectionItem
        {
            Id = reader.GetInt32(0),
            AccountId = reader.GetInt32(1),
            Name = reader.GetString(2),
            TransactionDate = DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind),
            Deposit = reader.IsDBNull(4) ? null : decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            Payment = reader.IsDBNull(5) ? null : decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            Periodicity = (Periodicity)reader.GetInt32(6),
            MatchText = reader.IsDBNull(7) ? null : reader.GetString(7),
            NeedsReview = reader.GetInt32(8) != 0,
            UpdatedAtUtc = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
        };
    }
}
