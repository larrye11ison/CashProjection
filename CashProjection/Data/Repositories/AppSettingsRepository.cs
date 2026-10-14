namespace CashProjection.Data.Repositories
{
    // Simple key/value store for small pieces of app state that aren't
    // worth their own table (Plaid environment selection, sync cursor, etc.)
    public sealed class AppSettingsRepository
    {
        private readonly CashProjectionDbContext _db;

        public AppSettingsRepository(CashProjectionDbContext db) => _db = db;

        public string? Get(string key)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key;";
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() as string;
        }

        public void Set(string key, string value)
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$value", value);
            cmd.ExecuteNonQuery();
        }
    }
}
