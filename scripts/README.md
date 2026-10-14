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
