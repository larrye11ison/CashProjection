using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class ChangeHistoryRepository
    {
        private readonly CashProjectionDbContext _db;

        public ChangeHistoryRepository(CashProjectionDbContext db) => _db = db;

        public void Insert(ChangeHistoryEntry entry)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ChangeHistory (AccountId, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId)
                VALUES ($accountId, $entityType, $entityId, $changeType, $before, $after, $description, $timestamp, $batchId);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$accountId", entry.AccountId);
            cmd.Parameters.AddWithValue("$entityType", entry.EntityType.ToString());
            cmd.Parameters.AddWithValue("$entityId", entry.EntityId);
            cmd.Parameters.AddWithValue("$changeType", entry.ChangeType.ToString());
            cmd.Parameters.AddWithValue("$before", (object?)entry.BeforeJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$after", (object?)entry.AfterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$description", entry.Description);
            cmd.Parameters.AddWithValue("$timestamp", entry.TimestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$batchId", (object?)entry.BatchId ?? DBNull.Value);
            entry.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        public ChangeHistoryEntry? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public List<ChangeHistoryEntry> GetAllNewestFirst(int accountId)
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE AccountId = $accountId ORDER BY Id DESC;
                """;
            cmd.Parameters.AddWithValue("$accountId", accountId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        // Entries strictly newer than the given point in time, for this
        // account only, newest first — the set that RollbackTo() reverse-
        // applies in order.
        public List<ChangeHistoryEntry> GetNewerThan(DateTime timestampUtc, int accountId)
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE TimestampUtc > $t AND AccountId = $accountId ORDER BY Id DESC;
                """;
            cmd.Parameters.AddWithValue("$t", timestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$accountId", accountId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        // All entries recorded as part of the same logical user action
        // (see ChangeHistoryEntry.BatchId), oldest first. Not filtered by
        // account — a batch is always recorded for a single account's
        // entities in one logical action, so there's nothing to filter.
        public List<ChangeHistoryEntry> GetByBatchId(string batchId)
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, AccountId, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE BatchId = $batchId ORDER BY Id ASC;
                """;
            cmd.Parameters.AddWithValue("$batchId", batchId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        private static ChangeHistoryEntry Read(SqliteDataReader reader) => new ChangeHistoryEntry
        {
            Id = reader.GetInt32(0),
            AccountId = reader.GetInt32(1),
            EntityType = Enum.Parse<ChangeEntityType>(reader.GetString(2)),
            EntityId = reader.GetInt32(3),
            ChangeType = Enum.Parse<ChangeType>(reader.GetString(4)),
            BeforeJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            AfterJson = reader.IsDBNull(6) ? null : reader.GetString(6),
            Description = reader.GetString(7),
            TimestampUtc = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            BatchId = reader.IsDBNull(9) ? null : reader.GetString(9),
        };
    }
}
