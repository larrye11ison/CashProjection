// One-off, manually-run tool. See scripts/README.md.
//
// Backfills BankTransactions.Description in a SQLite database with Plaid's
// OriginalDescription (the bank's own raw text) wherever it's available and
// differs from what's currently stored. Plaid's Name/MerchantName fields are
// cleaned-up/enriched versions that can drop real detail (e.g. "AT&T"
// instead of the bank's own "BILL PAY AT&T Mobility") — this exists to
// recover that detail for transactions already synced into the database
// under the old mapping.
//
// Does a full /transactions/sync walk from an empty cursor requesting
// Options.IncludeOriginalDescription — this only UPDATES Description on
// existing rows matched by PlaidTransactionId; it never inserts new rows.
// Note: Options.DaysRequested has no effect here — per Plaid's own docs, it
// only applies to an Item where the Transactions product hasn't been
// initialized yet, which isn't the case for an Item this app has already
// been syncing. So this can only recover original descriptions for
// transactions Plaid still has on file for this Item, i.e. the same
// transaction history already in the database — it can't pull in older
// history than that.
//
// Defaults to a dry run (reports what WOULD change, writes nothing). Pass
// --apply to actually write.
//
// Usage:
//   dotnet run --db "C:\path\to\your-copy.sqlite"            (dry run)
//   dotnet run --db "C:\path\to\your-copy.sqlite" --apply     (writes changes)

using System.Runtime.InteropServices;
using System.Text;
using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Transactions;
using Microsoft.Data.Sqlite;

const uint CredTypeGeneric = 1;

string? dbPath = null;
var apply = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--db":
            if (i + 1 >= args.Length)
                return Fail("--db requires a path argument.");
            dbPath = args[++i];
            break;
        case "--apply":
            apply = true;
            break;
        default:
            return Fail($"Unrecognized argument: {args[i]}");
    }
}

if (dbPath is null)
    return Fail("Usage: dotnet run --db <path-to-sqlite-copy> [--apply]");

if (!File.Exists(dbPath))
    return Fail($"No file found at: {dbPath}");

// Guard against accidentally pointing this at the real, live database — this
// tool is meant to run against the copy you made, not the file the app
// itself reads and writes.
var liveDbCandidates = new[]
{
    System.Environment.GetEnvironmentVariable("OneDrive") is { Length: > 0 } oneDrive
        ? Path.Combine(oneDrive, "CashProjection.sqlite")
        : null,
    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "CashProjection.sqlite"),
};
var fullDbPath = Path.GetFullPath(dbPath);
if (liveDbCandidates.Any(c => c is not null && string.Equals(Path.GetFullPath(c), fullDbPath, StringComparison.OrdinalIgnoreCase)))
    return Fail("That path looks like the app's live database, not a copy. Point --db at your copy instead.");

Console.WriteLine(apply ? $"APPLY mode — writing changes to: {dbPath}" : $"DRY RUN — no changes will be written. Target: {dbPath}");
Console.WriteLine();

var clientId = ReadCredential("Plaid:Production:ClientId")
    ?? throw new InvalidOperationException("No credential 'CashProjection:Plaid:Production:ClientId' in Windows Credential Manager.");
var secret = ReadCredential("Plaid:Production:Secret")
    ?? throw new InvalidOperationException("No credential 'CashProjection:Plaid:Production:Secret' in Windows Credential Manager.");
var accessToken = ReadCredential("Plaid:Production:AccessToken")
    ?? throw new InvalidOperationException("No credential 'CashProjection:Plaid:Production:AccessToken' in Windows Credential Manager.");

var client = new PlaidClient(Going.Plaid.Environment.Production, clientId: clientId, secret: secret);

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

var fetched = 0;
var matchedLocally = 0;
var updated = 0;
var alreadyCurrent = 0;
var noOriginalDescription = 0;
var notFoundLocally = 0;

string? cursor = null;
var hasMore = true;

while (hasMore)
{
    var response = await client.TransactionsSyncAsync(new TransactionsSyncRequest
    {
        AccessToken = accessToken,
        Cursor = cursor,
        Options = new TransactionsSyncRequestOptions
        {
            IncludeOriginalDescription = true,
        },
    });

    if (response.Error is not null)
        return Fail($"Plaid error: {response.Error.ErrorCode} — {response.Error.ErrorMessage}");

    foreach (var t in response.Added.Concat(response.Modified))
    {
        if (string.IsNullOrEmpty(t.TransactionId))
            continue;

        fetched++;

        if (string.IsNullOrWhiteSpace(t.OriginalDescription))
        {
            noOriginalDescription++;
            continue;
        }

        var currentDescription = GetCurrentDescription(connection, t.TransactionId);
        if (currentDescription is null)
        {
            notFoundLocally++;
            continue;
        }

        matchedLocally++;

        if (currentDescription == t.OriginalDescription)
        {
            alreadyCurrent++;
            continue;
        }

        Console.WriteLine($"  [{t.TransactionId}] '{currentDescription}' -> '{t.OriginalDescription}'");

        if (apply)
            UpdateDescription(connection, t.TransactionId, t.OriginalDescription);

        updated++;
    }

    cursor = response.NextCursor;
    hasMore = response.HasMore;
}

Console.WriteLine();
Console.WriteLine("--- Summary ---");
Console.WriteLine($"Fetched from Plaid:          {fetched}");
Console.WriteLine($"Matched to a local row:      {matchedLocally}");
Console.WriteLine($"  Already up to date:        {alreadyCurrent}");
Console.WriteLine($"  {(apply ? "Updated" : "Would update")}:{(apply ? "" : " ")}                 {updated}");
Console.WriteLine($"No OriginalDescription:      {noOriginalDescription}");
Console.WriteLine($"Not found in local database: {notFoundLocally}");

if (!apply && updated > 0)
{
    Console.WriteLine();
    Console.WriteLine("This was a dry run — re-run with --apply to write these changes.");
}

return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static string? GetCurrentDescription(SqliteConnection connection, string plaidTransactionId)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT Description FROM BankTransactions WHERE PlaidTransactionId = $id;";
    cmd.Parameters.AddWithValue("$id", plaidTransactionId);
    return cmd.ExecuteScalar() as string;
}

// Deliberately touches only Description — BankTransactions.VendorName is a
// separate column (holding Plaid's cleaned-up Name/MerchantName) and is
// never written here, regardless of what else the table's schema grows.
static void UpdateDescription(SqliteConnection connection, string plaidTransactionId, string description)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "UPDATE BankTransactions SET Description = $description WHERE PlaidTransactionId = $id;";
    cmd.Parameters.AddWithValue("$description", description);
    cmd.Parameters.AddWithValue("$id", plaidTransactionId);
    cmd.ExecuteNonQuery();
}

// Minimal read-only mirror of CashProjection's
// WindowsCredentialManagerSecretStore.Get — this script is self-contained
// rather than referencing the main app project, so it doesn't pull in the
// whole WPF dependency graph for one read-only lookup.
static string? ReadCredential(string key)
{
    var target = "CashProjection:" + key;
    if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        return null;

    try
    {
        var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
        if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            return null;

        var bytes = new byte[credential.CredentialBlobSize];
        Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
        return Encoding.Unicode.GetString(bytes);
    }
    finally
    {
        CredFree(credentialPtr);
    }
}

[DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

[DllImport("Advapi32.dll", EntryPoint = "CredFree")]
static extern void CredFree(IntPtr cred);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct CREDENTIAL
{
    public uint Flags;
    public uint Type;
    public IntPtr TargetName;
    public IntPtr Comment;
    public long LastWritten;
    public uint CredentialBlobSize;
    public IntPtr CredentialBlob;
    public uint Persist;
    public uint AttributeCount;
    public IntPtr Attributes;
    public IntPtr TargetAlias;
    public IntPtr UserName;
}
