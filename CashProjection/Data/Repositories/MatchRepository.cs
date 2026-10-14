using System.Globalization;
using Microsoft.Data.Sqlite;
using CashProjection.Models;

namespace CashProjection.Data.Repositories
{
    public sealed class MatchRepository
    {
        private readonly CashProjectionDbContext _db;

        public MatchRepository(CashProjectionDbContext db) => _db = db;

        // Any existing row for this pair — Presumptive, Confirmed, or
        // Rejected — means the matching engine should never suggest it
        // again, so this single check covers both "already found" and
        // "previously rejected" exclusion.
        public bool ExistsForPair(int projectionItemId, int bankTransactionId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Matches WHERE ProjectionItemId = $pid AND BankTransactionId = $bid;";
            cmd.Parameters.AddWithValue("$pid", projectionItemId);
            cmd.Parameters.AddWithValue("$bid", bankTransactionId);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public void Insert(Match match)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Matches (ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate)
                VALUES ($pid, $bid, $status, $score, $created, $decided, $matchedAtItemDate);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$pid", match.ProjectionItemId);
            cmd.Parameters.AddWithValue("$bid", match.BankTransactionId);
            cmd.Parameters.AddWithValue("$status", match.Status.ToString());
            cmd.Parameters.AddWithValue("$score", match.Score);
            cmd.Parameters.AddWithValue("$created", match.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue(
                "$decided",
                match.DecidedAtUtc.HasValue ? match.DecidedAtUtc.Value.ToString("O") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "$matchedAtItemDate",
                match.MatchedAtItemDate.HasValue ? match.MatchedAtItemDate.Value.ToString("O") : (object)DBNull.Value);
            match.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Full-field update by Id — used for Accept/Reject decisions and for
        // Undo/Redo restoring a prior snapshot. Deliberately not a narrower
        // "UpdateStatus" that only touches Status/DecidedAtUtc: that shape
        // previously caused MatchedAtItemDate to be silently dropped on
        // every Undo/Redo of a match decision.
        public void Update(Match match)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Matches
                SET Status = $status,
                    Score = $score,
                    DecidedAtUtc = $decided,
                    MatchedAtItemDate = $matchedAtItemDate
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", match.Id);
            cmd.Parameters.AddWithValue("$status", match.Status.ToString());
            cmd.Parameters.AddWithValue("$score", match.Score);
            cmd.Parameters.AddWithValue(
                "$decided",
                match.DecidedAtUtc.HasValue ? match.DecidedAtUtc.Value.ToString("O") : (object)DBNull.Value);
            cmd.Parameters.AddWithValue(
                "$matchedAtItemDate",
                match.MatchedAtItemDate.HasValue ? match.MatchedAtItemDate.Value.ToString("O") : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public Match? GetById(int id)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate
                FROM Matches WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        public Match? GetConfirmedForProjectionItem(int projectionItemId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate
                FROM Matches WHERE ProjectionItemId = $pid AND Status = 'Confirmed' LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$pid", projectionItemId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        // A bank transaction that's already Confirmed against some item is
        // spoken for — it must never be offered as a candidate for a
        // *different* projection item, even one that scores well against it.
        public Match? GetConfirmedForBankTransaction(int bankTransactionId)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate
                FROM Matches WHERE BankTransactionId = $bid AND Status = 'Confirmed' LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$bid", bankTransactionId);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        // Other still-open (Presumptive) rows touching either side of a pair
        // that's about to be confirmed. Once one match for an item or a
        // transaction is accepted, every other open suggestion involving
        // either of them is stale and needs to be superseded rather than
        // left sitting in the review queue.
        public List<Match> GetOtherPresumptiveMatches(int excludeMatchId, int projectionItemId, int bankTransactionId)
        {
            var results = new List<Match>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate
                FROM Matches
                WHERE Status = 'Presumptive' AND Id != $excludeId
                  AND (ProjectionItemId = $pid OR BankTransactionId = $bid);
                """;
            cmd.Parameters.AddWithValue("$excludeId", excludeMatchId);
            cmd.Parameters.AddWithValue("$pid", projectionItemId);
            cmd.Parameters.AddWithValue("$bid", bankTransactionId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        public List<Match> GetByStatus(MatchStatus status)
        {
            var results = new List<Match>();
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProjectionItemId, BankTransactionId, Status, Score, CreatedAtUtc, DecidedAtUtc, MatchedAtItemDate
                FROM Matches WHERE Status = $status ORDER BY Score DESC;
                """;
            cmd.Parameters.AddWithValue("$status", status.ToString());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(Read(reader));
            return results;
        }

        private static Match Read(SqliteDataReader reader) => new Match
        {
            Id = reader.GetInt32(0),
            ProjectionItemId = reader.GetInt32(1),
            BankTransactionId = reader.GetInt32(2),
            Status = Enum.Parse<MatchStatus>(reader.GetString(3)),
            Score = reader.GetInt32(4),
            CreatedAtUtc = DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            DecidedAtUtc = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind),
            MatchedAtItemDate = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind),
        };
    }
}
