// Run once, manually, when ready to cut over from the legacy JSON file to
// the new SQLite database. Not invoked by the app. Safe to delete after use.
//
// Reads the legacy CashProjection.json (READ-ONLY — never modified) and
// imports its Transactions as ProjectionItems into CashProjection.sqlite,
// replacing whatever placeholder/test projection items are currently there
// for the active account.
//
// Deliberately does NOT touch Accounts.StartingBalance: that is now
// authoritatively sourced from Plaid on every sync, not from the JSON's
// old manually-entered InitialBalance, which would be a regression to
// overwrite it with here.
//
// Usage: dotnet run [jsonPath] [dbPath]
//   Defaults to %OneDrive%\CashProjection.json and %OneDrive%\CashProjection.sqlite.

using System.Globalization;
using Microsoft.Data.Sqlite;
using System.Text.Json;

var oneDrive = Environment.GetEnvironmentVariable("OneDrive") ?? "";
var jsonPath = args.Length > 0 ? args[0] : Path.Combine(oneDrive, "CashProjection.json");
var dbPath = args.Length > 1 ? args[1] : Path.Combine(oneDrive, "CashProjection.sqlite");

if (!File.Exists(jsonPath))
{
    Console.WriteLine($"JSON file not found: {jsonPath}");
    return;
}

if (!File.Exists(dbPath))
{
    Console.WriteLine($"SQLite database not found: {dbPath}");
    return;
}

Console.WriteLine($"Reading (read-only): {jsonPath}");
var json = File.ReadAllText(jsonPath);
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;

var accountName = root.TryGetProperty("AccountName", out var an) ? an.GetString() : null;

var items = new List<(string Name, DateTime Date, decimal? Deposit, decimal? Payment, int Periodicity)>();
foreach (var t in root.GetProperty("Transactions").EnumerateArray())
{
    items.Add((
        t.GetProperty("Name").GetString() ?? string.Empty,
        t.GetProperty("TransactionDate").GetDateTime(),
        t.TryGetProperty("Deposit", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetDecimal() : (decimal?)null,
        t.TryGetProperty("Payment", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetDecimal() : (decimal?)null,
        t.GetProperty("Periodicity").GetInt32()
    ));
}
Console.WriteLine($"Parsed {items.Count} transaction(s) from JSON.");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
using var tx = conn.BeginTransaction();

int accountId;
using (var cmd = conn.CreateCommand())
{
    cmd.Transaction = tx;
    cmd.CommandText = "SELECT Id FROM Accounts WHERE IsActive = 1 LIMIT 1;";
    var result = cmd.ExecuteScalar();
    if (result is null)
    {
        Console.WriteLine("No active account found — run the app at least once first, then re-run this script.");
        tx.Rollback();
        return;
    }
    accountId = Convert.ToInt32(result);
}

if (!string.IsNullOrWhiteSpace(accountName))
{
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "UPDATE Accounts SET Name = $name WHERE Id = $id;";
    cmd.Parameters.AddWithValue("$name", accountName);
    cmd.Parameters.AddWithValue("$id", accountId);
    cmd.ExecuteNonQuery();
    Console.WriteLine($"Updated account name to '{accountName}'. StartingBalance left untouched (Plaid is the source of truth for that now).");
}

using (var cmd = conn.CreateCommand())
{
    cmd.Transaction = tx;
    cmd.CommandText = "DELETE FROM ProjectionItems WHERE AccountId = $id;";
    cmd.Parameters.AddWithValue("$id", accountId);
    var deleted = cmd.ExecuteNonQuery();
    Console.WriteLine($"Removed {deleted} existing projection item(s) before import.");
}

foreach (var item in items)
{
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO ProjectionItems (AccountId, Name, ItemDate, Deposit, Payment, Periodicity, MatchText, NeedsReview, UpdatedAtUtc)
        VALUES ($accountId, $name, $itemDate, $deposit, $payment, $periodicity, NULL, 0, $updatedAtUtc);
        """;
    cmd.Parameters.AddWithValue("$accountId", accountId);
    cmd.Parameters.AddWithValue("$name", item.Name);
    cmd.Parameters.AddWithValue("$itemDate", item.Date.ToString("O"));
    cmd.Parameters.AddWithValue(
        "$deposit",
        item.Deposit.HasValue ? item.Deposit.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
    cmd.Parameters.AddWithValue(
        "$payment",
        item.Payment.HasValue ? item.Payment.Value.ToString(CultureInfo.InvariantCulture) : (object)DBNull.Value);
    cmd.Parameters.AddWithValue("$periodicity", item.Periodicity);
    cmd.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
    cmd.ExecuteNonQuery();
}

tx.Commit();
Console.WriteLine($"Imported {items.Count} projection item(s) into account Id={accountId}.");
Console.WriteLine("Done. The legacy JSON file was only read, never modified.");
