using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace CashProjection.Data
{
    // Owns the SQLite connection for the whole app lifetime and applies the
    // embedded *.sql migration files under Data/Migrations on startup.
    // GetDefaultDbPath() always resolves to "CashProjection.sqlite" — it must
    // never resolve to "CashProjection.json" (that file belongs to the legacy
    // PersistenceService and is never touched by this class).
    public sealed class CashProjectionDbContext : IDisposable
    {
        public SqliteConnection Connection { get; }

        public CashProjectionDbContext()
            : this(GetDefaultDbPath())
        {
        }

        public CashProjectionDbContext(string dbPath)
        {
            Connection = new SqliteConnection($"Data Source={dbPath}");
            Connection.Open();

            using (var pragma = Connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }

            EnsureCreated();
        }

        public static string GetDefaultDbPath()
        {
            // Prefer OneDrive, fallback to Documents — mirrors
            // PersistenceService.GetDefaultFilePath(), but with a distinct
            // filename so the legacy JSON file is never collided with.
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            var baseDir = !string.IsNullOrWhiteSpace(oneDrive)
                ? oneDrive!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(baseDir, "CashProjection.sqlite");
        }

        // Migration files are numbered (001_, 002_, ...) and tracked via the
        // SchemaVersion table, so each one runs exactly once ever — required
        // because not every migration is naturally idempotent (e.g. ALTER
        // TABLE ADD COLUMN fails if run a second time), unlike the
        // CREATE TABLE IF NOT EXISTS statements the earliest migrations use.
        private void EnsureCreated()
        {
            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL);";
                cmd.ExecuteNonQuery();
            }

            int currentVersion;
            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1;";
                var result = cmd.ExecuteScalar();
                currentVersion = result is null ? 0 : Convert.ToInt32(result);
            }

            var assembly = Assembly.GetExecutingAssembly();
            var migrations = assembly
                .GetManifestResourceNames()
                .Where(n => n.Contains(".Data.Migrations.") && n.EndsWith(".sql"))
                .Select(n => new { Name = n, Version = ParseVersion(n) })
                .Where(m => m.Version > currentVersion)
                .OrderBy(m => m.Version)
                .ToList();

            foreach (var migration in migrations)
            {
                using (var stream = assembly.GetManifestResourceStream(migration.Name)!)
                using (var reader = new StreamReader(stream))
                {
                    var sql = reader.ReadToEnd();
                    using var cmd = Connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }

                currentVersion = migration.Version;
            }

            using (var cmd = Connection.CreateCommand())
            {
                cmd.CommandText = """
                    DELETE FROM SchemaVersion;
                    INSERT INTO SchemaVersion (Version) VALUES ($v);
                    """;
                cmd.Parameters.AddWithValue("$v", currentVersion);
                cmd.ExecuteNonQuery();
            }
        }

        private static int ParseVersion(string resourceName)
        {
            var parts = resourceName.Split('.');
            var baseName = parts.Length >= 2 ? parts[^2] : string.Empty;
            var digits = new string(baseName.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var v) ? v : 0;
        }

        public void Dispose() => Connection.Dispose();
    }
}
