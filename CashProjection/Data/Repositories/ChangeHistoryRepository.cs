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
                INSERT INTO ChangeHistory (EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId)
                VALUES ($entityType, $entityId, $changeType, $before, $after, $description, $timestamp, $batchId);
                SELECT last_insert_rowid();
                """;
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
                SELECT Id, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public List<ChangeHistoryEntry> GetAllNewestFirst()
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory ORDER BY Id DESC;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        // Entries strictly newer than the given point in time, newest first
        // — the set that RollbackTo() reverse-applies in order.
        public List<ChangeHistoryEntry> GetNewerThan(DateTime timestampUtc)
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
                FROM ChangeHistory WHERE TimestampUtc > $t ORDER BY Id DESC;
                """;
            cmd.Parameters.AddWithValue("$t", timestampUtc.ToString("O"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        // All entries recorded as part of the same logical user action
        // (see ChangeHistoryEntry.BatchId), oldest first.
        public List<ChangeHistoryEntry> GetByBatchId(string batchId)
        {
            var results = new List<ChangeHistoryEntry>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, EntityType, EntityId, ChangeType, BeforeJson, AfterJson, Description, TimestampUtc, BatchId
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
            EntityType = Enum.Parse<ChangeEntityType>(reader.GetString(1)),
            EntityId = reader.GetInt32(2),
            ChangeType = Enum.Parse<ChangeType>(reader.GetString(3)),
            BeforeJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            AfterJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            Description = reader.GetString(6),
            TimestampUtc = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
            BatchId = reader.IsDBNull(8) ? null : reader.GetString(8),
        };
    }
}
