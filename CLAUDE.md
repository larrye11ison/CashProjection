# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Cash Projection — a single-maintainer WinUI 3 (Windows App SDK, unpackaged) desktop app on .NET 10.0. MVVM via CommunityToolkit.Mvvm. Local SQLite database (OneDrive-synced). Syncs real bank transactions via Plaid (`Going.Plaid` client).

Single project: `CashProjection/CashProjection.csproj` (referenced by `CashProjection.slnx`). Key folders: `Models/`, `ViewModels/`, `Views/`, `Data/` + `Data/Migrations/*.sql`, `Services/Plaid/`, `Services/Secrets/` (Windows Credential Manager for production creds, `DevFileSecretStore` for sandbox), `Services/Matching/` (fuzzy transaction matching). `scripts/` contains vestigial one-off migration tools — not part of the solution, ignore them.

## Features 

### General 

The app tracks both "real bank transactions" as well as "projection items" for all bank accounts available from the configured Plaid connection. These will often be called simply "transactions" and "projections." The Transactions are items that were downloaded from the user's bank via Plaid. The Projections are scheduled future transactions, created and managed by the user.

### Matching

There is a "matching" feature within the app that helps the user reconcile/match up projections with real transactions. It uses some fuzzy string matching to present presumptive matches to the user. When they "accept" a suggested linkage in this interface, the app in effect marks that bank transaction as reconciled, meaning it won't be considered as a possible match in the future. In addition, the linked projection item in the matched pair is "pushed out," meaning its date is rescheduled for a future date based on its periodicity config.

### Pending Transactions

The transactional detail from plaid regularly lags behind the account balances, often for several hours. This means that the balance obtained by plaid often cannot be supported by the currently available actual transactional detail. When the app detects this scenario, it will show a "pending transactions" item in the account details to let the user know that some details are missing.

### Balance Correction

Apparently it is common for Plaid to not provide a running balance for each transaction item (this also may be a limitation of some banks, I'm not sure). The account balance is just a point in time item that - again - can be very out of date with the transactional detail. In effect, the app has to guess at which balance belongs to which transaction. There may be other problematic scenarios as well. To address these kinds of issues, a balance correction mechanism was added to the app. This allows the user to force the running balance on a specific bank transaction item, which will then be used to recalculate the running balance from that point forward. 

## Design rules

- Don't do a modal popup window; instead, use "pop-over" mechanism that's already present in the app.
- For any model popover, ensure that the input focus is set in the popover's first input control. Be absolutely certain that this is true for *keyboard input* - calling "Control.Focus()" is not enough.

## Build

WinUI3/Windows App SDK requires an explicit runtime identifier — there is no AnyCPU. **Always build with:**
```
dotnet build CashProjection/CashProjection.csproj -r win-x64
```
(or `-r win-arm64`). Never use `-p:Platform=x64` — it does not update the user's EXE.

No test project exists in this repo. No linter or formatter is configured (no `.editorconfig`).

## Plaid credentials

- **Sandbox**: read from gitignored `plaid.sandbox.secrets.json` at repo root by `DevFileSecretStore`. The app won't run past first launch without it.
- **Production**: entered via in-app Plaid Settings UI, stored in Windows Credential Manager — never in a file.
- The encrypted Plaid connection export/import bundles the access token, item ID, *and* the production client_id/secret, so moving to a second machine doesn't burn another Plaid Item slot. Sandbox creds are the exception — still sourced locally per machine. The "not burning another plaid item slot" is critical - we want to take great precautions to avoid creating a situation where the user might even accidentally disconnect from their bank and then be forced to reauthorise their bank account through plaid.

## Publish gotchas

- `dotnet publish` can silently drop `CashProjection.pri` (compiled-XAML resource index), causing an `XamlParseException` crash on other machines. Fixed via a custom `IncludeProjectPriFileInPublish` target in the `.csproj` — don't remove it.
- `WindowsAppSDKSelfContained` is `false` — both the .NET runtime and the Windows App SDK runtime are framework-dependent, so **any machine this app runs on needs the Windows App Runtime redistributable installed** (not just .NET 10). If it's ever flipped back to `true` for a specific deploy target that lacks it, the `RemoveNonEnglishWindowsAppSdkResources` trimming target (removed when this was turned off — see git history) will need restoring too, or every publish balloons with ~90 near-useless locale `.mui` folders.

## Workflow rules

- **Never launch or screenshot the app to verify changes.** Edit, build, and stop — the user tests it themselves (the app touches real production Plaid data on launch).
- **Never push to any remote** (including after merging to main) without explicit in-the-moment instruction, even if a push was approved earlier in the session.
- Branch naming: `issue-N-slug` when the work is tied to a specific GitHub issue, otherwise `feature/slug` or `bug/slug` as appropriate.
