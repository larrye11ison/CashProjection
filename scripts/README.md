# scripts/

One-time, manually-run utilities. Nothing here is invoked by the app, built
into its output, or shipped — these exist purely as reviewable artifacts for
operations you run once and then can delete.

## Migrate-JsonToSqlite.ps1

Imports projection items from the legacy `CashProjection.json` file into the
new SQLite database, replacing whatever projection items are currently
there for the active account. Only reads the JSON file — never modifies or
deletes it. Does not touch the account's starting balance, since that's
sourced from Plaid now, not the JSON's old manually-entered value.

Run it with the app closed:

```powershell
.\Migrate-JsonToSqlite.ps1
```

## BackfillOriginalDescriptions

Backfills `BankTransactions.Description` with Plaid's `OriginalDescription`
(the bank's own raw text) for transactions already synced into the
database under the old `Name`/`MerchantName` mapping, which can be
Plaid's own cleaned-up version and drop real detail (e.g. `"AT&T"` instead
of the bank's own `"BILL PAY AT&T Mobility"`).

Only updates `Description` on existing rows matched by
`PlaidTransactionId` — never inserts new rows, never touches anything
else. Reads Plaid Production credentials from Windows Credential Manager,
the same place the app itself stores them, so no separate secrets setup
is needed.

**Run this against a copy of the database, not the live file** — point
`--db` at the copy. The tool refuses to run if `--db` resolves to the
app's actual live database path, as a backstop.

Defaults to a dry run (prints what it would change, writes nothing):

```powershell
dotnet run --project BackfillOriginalDescriptions --db "C:\path\to\your-copy.sqlite"
```

Add `--apply` once you've reviewed the dry-run output and are ready to
write the changes:

```powershell
dotnet run --project BackfillOriginalDescriptions --db "C:\path\to\your-copy.sqlite" --apply
```

Note: this can only recover descriptions for transactions Plaid still has
on file for this Item — i.e. the same transaction history already in the
database. Plaid's `Options.DaysRequested` (which can extend lookback) has
no effect on an Item that's already been actively syncing, so this can't
pull in *older* history than what's already there.
