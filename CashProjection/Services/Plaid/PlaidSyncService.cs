using System.Linq;
using CashProjection.Data.Repositories;
using CashProjection.Models;
using CashProjection.Services.Secrets;
using CashProjection.ViewModels;

namespace CashProjection.Services.Plaid
{
    public sealed class PlaidSyncService
    {
        private readonly PlaidClientFactory _clientFactory;
        private readonly SecretStoreFactory _secretStores;
        private readonly PlaidSettingsService _settings;
        private readonly BankTransactionRepository _bankTransactionRepo;
        private readonly AccountRepository _accountRepo;
        private readonly UnseenTransactionsTracker _unseenTracker;
        private readonly StatusBarViewModel? _statusVM;

        public PlaidSyncService(
            PlaidClientFactory clientFactory,
            SecretStoreFactory secretStores,
            PlaidSettingsService settings,
            BankTransactionRepository bankTransactionRepo,
            AccountRepository accountRepo,
            UnseenTransactionsTracker unseenTracker,
            StatusBarViewModel? statusVM = null)
        {
            _clientFactory = clientFactory;
            _secretStores = secretStores;
            _settings = settings;
            _bankTransactionRepo = bankTransactionRepo;
            _accountRepo = accountRepo;
            _unseenTracker = unseenTracker;
            _statusVM = statusVM;
        }

        // Creates a local Account row for every account Plaid reports under
        // the linked Item that doesn't have one yet — e.g. a savings or
        // money-market share that was never explicitly added before. Best-
        // effort: any Plaid error here is swallowed rather than surfaced,
        // since this is a background convenience and SyncAsync (called
        // right after, in the same background-sync pass) will still run
        // and report its own errors normally either way. New accounts
        // start IsActive=false — only ever flips the account the app
        // defaults to showing at launch, never multiple at once.
        public async Task DiscoverAccountsAsync()
        {
            var environment = _settings.GetEnvironment();
            var accessToken = _secretStores.AccessTokenStore.Get(PlaidLinkService.AccessTokenKey(environment));
            if (accessToken is null)
                return;

            try
            {
                var client = _clientFactory.Create();
                var response = await client.AccountsGetAsync(new Going.Plaid.Accounts.AccountsGetRequest
                {
                    AccessToken = accessToken,
                });
                if (response.Error is not null)
                    return;

                var itemId = response.Item?.ItemId;

                foreach (var plaidAccount in response.Accounts)
                {
                    if (plaidAccount.AccountId is null)
                        continue;
                    if (_accountRepo.GetByPlaidAccountId(plaidAccount.AccountId) is not null)
                        continue;

                    _accountRepo.Insert(new Account
                    {
                        Name = plaidAccount.Name ?? plaidAccount.OfficialName ?? "Linked Account",
                        IsActive = false,
                        PlaidItemId = itemId,
                        PlaidAccountId = plaidAccount.AccountId,
                        StartingBalance = plaidAccount.Balances?.Current ?? 0m,
                        StartingBalanceAsOf = DateTime.UtcNow,
                    });
                }
            }
            catch
            {
                // Best-effort — see method summary.
            }
        }

        // Syncs every account passed in that's linked to the same Plaid
        // Item in a single pass. The sync cursor is Item-scoped, not
        // account-scoped, so this always does exactly one
        // /transactions/sync walk no matter how many local Account rows
        // are passed in — each transaction gets routed to whichever of
        // them its own account_id matches.
        // Returns the number of bank-transaction rows added/modified/removed
        // by this pass, so callers can skip UI work that's only meaningful
        // when something actually changed (most 10-minute periodic syncs
        // find nothing new).
        public async Task<int> SyncAsync(IReadOnlyList<Account> accounts)
        {
            if (accounts.Count == 0)
                return 0;

            var environment = _settings.GetEnvironment();
            var accessToken = _secretStores.AccessTokenStore.Get(PlaidLinkService.AccessTokenKey(environment));
            if (accessToken is null)
                return 0;

            _statusVM?.Report("Syncing transactions from your bank...", busy: true);

            try
            {
                var client = _clientFactory.Create();

                // A single Item/access_token can be authorized for several
                // of the institution's accounts at once (checking, savings,
                // a CD, a line of credit, ...) — /transactions/sync returns
                // all of them undifferentiated. Pin down which Plaid
                // account each passed-in Account row represents, once, so
                // every sync after that can route transactions correctly
                // instead of guessing.
                if (accounts.Any(a => string.IsNullOrEmpty(a.PlaidAccountId)))
                {
                    var accountsResponse = await client.AccountsGetAsync(new Going.Plaid.Accounts.AccountsGetRequest
                    {
                        AccessToken = accessToken,
                    });
                    if (accountsResponse.Error is not null)
                        throw new InvalidOperationException(
                            $"Plaid error listing accounts: {accountsResponse.Error.ErrorCode} — {accountsResponse.Error.ErrorMessage}");

                    foreach (var account in accounts.Where(a => string.IsNullOrEmpty(a.PlaidAccountId)))
                    {
                        // Some institutions report more than one account with
                        // Subtype == Checking (seen in practice: a money-market
                        // share miscategorized as "Checking" alongside the real
                        // checking share) — picking "the first one" in that case
                        // would silently wire the app up to the wrong account.
                        // Fail loudly instead of guessing. This bootstrap path
                        // only matters for the checking account — every other
                        // account is created with its PlaidAccountId already
                        // set, so it never hits this branch.
                        var checkingAccounts = accountsResponse.Accounts
                            .Where(a => a.Subtype == Going.Plaid.Entity.AccountSubtype.Checking)
                            .ToList();
                        if (checkingAccounts.Count == 0)
                            throw new InvalidOperationException(
                                "Plaid did not report a checking account on this Item — cannot determine which account to sync.");
                        if (checkingAccounts.Count > 1)
                            throw new InvalidOperationException(
                                "Plaid reported more than one account with subtype Checking on this Item, so the correct one can't be picked automatically: " +
                                string.Join(", ", checkingAccounts.Select(a => $"'{a.Name}' (mask {a.Mask}, id {a.AccountId})")) +
                                ". Set Account.PlaidAccountId manually to the right one.");

                        account.PlaidAccountId = checkingAccounts[0].AccountId;
                        _accountRepo.Update(account);
                    }
                }

                var accountsByPlaidId = accounts
                    .Where(a => !string.IsNullOrEmpty(a.PlaidAccountId))
                    .ToDictionary(a => a.PlaidAccountId!);

                var cursor = _settings.GetSyncCursor();
                var hasMore = true;
                var totalChanged = 0;

                while (hasMore)
                {
                    var response = await client.TransactionsSyncAsync(new Going.Plaid.Transactions.TransactionsSyncRequest
                    {
                        AccessToken = accessToken,
                        Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                        // Without this, Plaid omits the raw bank string
                        // entirely — Name/MerchantName are Plaid's own
                        // cleaned-up versions, which can drop real detail
                        // the fuzzy matcher needs (e.g. "AT&T" instead of
                        // the bank's own "BILL PAY AT&T Mobility").
                        Options = new Going.Plaid.Entity.TransactionsSyncRequestOptions
                        {
                            IncludeOriginalDescription = true,
                        },
                    });

                    // Going.Plaid does not throw for Plaid-level API errors —
                    // they come back as a populated response.Error with a
                    // normal (non-exception) response. ITEM_LOGIN_REQUIRED
                    // specifically means the Item needs update-mode
                    // reconnect; surface that distinctly from a generic
                    // failure so the UI can point at "Reconnect Bank".
                    if (response.Error is not null)
                    {
                        if (response.Error.ErrorCode == "ITEM_LOGIN_REQUIRED")
                        {
                            _settings.SetNeedsReconnect(true);
                            _statusVM?.Report(
                                "Your bank connection needs to be reconnected — click \"Reconnect Bank\".",
                                busy: false,
                                StatusSeverity.Error);
                            return 0;
                        }

                        throw new InvalidOperationException(
                            $"Plaid error during sync: {response.Error.ErrorCode} — {response.Error.ErrorMessage}");
                    }

                    // Only transactions.Added are genuinely new — Modified
                    // covers things like a pending transaction posting,
                    // which the user has plausibly already seen — so only
                    // Added should light up the "unseen" indicator.
                    var addedIds = new HashSet<string>(
                        response.Added
                            .Where(a => !string.IsNullOrEmpty(a.TransactionId))
                            .Select(a => a.TransactionId!));

                    foreach (var t in response.Added.Concat(response.Modified))
                    {
                        if (string.IsNullOrEmpty(t.TransactionId))
                            continue;

                        // Route to whichever local Account this transaction's
                        // own account_id matches; if none do (e.g. a product
                        // at the institution with no Account row yet), skip
                        // it rather than guessing where it belongs.
                        if (t.AccountId is null || !accountsByPlaidId.TryGetValue(t.AccountId, out var account))
                            continue;

                        if (addedIds.Contains(t.TransactionId))
                            _unseenTracker.RegisterNewTransactions(account.Id, 1);

                        _bankTransactionRepo.Upsert(new BankTransaction
                        {
                            AccountId = account.Id,
                            PlaidTransactionId = t.TransactionId,
                            PlaidAccountId = t.AccountId,
                            TxnDate = t.Date.HasValue ? t.Date.Value.ToDateTime(TimeOnly.MinValue) : DateTime.Today,
                            // OriginalDescription is the bank's own raw text
                            // (requires Options.IncludeOriginalDescription,
                            // set above) — prefer it over Plaid's cleaned-up
                            // Name/MerchantName, which can strip detail the
                            // fuzzy matcher relies on. Not every transaction
                            // carries one (depends on the institution), so
                            // fall back to the cleaned-up fields when it's
                            // missing. Name is obsolete per Plaid's docs but
                            // is still the last-resort fallback when neither
                            // OriginalDescription nor MerchantName is set.
                            //
                            // VendorName is Plaid's cleaned-up name on its
                            // own, independent of OriginalDescription — kept
                            // as a separate column so the UI can show both
                            // when they diverge, and so FuzzyMatchingService
                            // can deliberately match on Description alone.
#pragma warning disable CS0612
                            Description = t.OriginalDescription ?? t.MerchantName ?? t.Name ?? "(unknown)",
                            VendorName = t.MerchantName ?? t.Name,
#pragma warning restore CS0612
                            Amount = t.Amount ?? 0m,
                            Pending = t.Pending ?? false,
                            Category = null,
                            RunningBalance = t.RunningBalance,
                            ImportedAtUtc = DateTime.UtcNow,
                        });
                        totalChanged++;
                    }

                    foreach (var removed in response.Removed)
                    {
                        _bankTransactionRepo.Remove(removed.TransactionId);
                        totalChanged++;
                    }

                    cursor = response.NextCursor;
                    hasMore = response.HasMore;
                }

                _settings.SetSyncCursor(cursor ?? string.Empty);
                _settings.SetNeedsReconnect(false);

                // Real-time balance refresh for every account, so each one's
                // displayed balance starts from the bank's actual number
                // rather than going stale.
                var balanceResponse = await client.AccountsBalanceGetAsync(new Going.Plaid.Accounts.AccountsBalanceGetRequest
                {
                    AccessToken = accessToken,
                });
                if (balanceResponse.Error is not null)
                    throw new InvalidOperationException(
                        $"Plaid error refreshing balance: {balanceResponse.Error.ErrorCode} — {balanceResponse.Error.ErrorMessage}");

                foreach (var plaidAccount in balanceResponse.Accounts)
                {
                    if (plaidAccount.AccountId is null || !accountsByPlaidId.TryGetValue(plaidAccount.AccountId, out var account))
                        continue;

                    account.StartingBalance = plaidAccount.Balances.Current ?? account.StartingBalance;
                    account.StartingBalanceAsOf = DateTime.UtcNow;
                    _accountRepo.Update(account);

                    // Persists a correct EstimatedBalance for every real
                    // transaction in this account's full history, so any
                    // window read later — the initial load, a scrolled-to
                    // page, or a Find-jumped window far from "now" — already
                    // carries a correct Balance with nothing further to
                    // compute. Cheap at realistic scale; run unconditionally
                    // rather than tracking whether this account's own
                    // transactions actually changed this pass.
                    _bankTransactionRepo.RecalculateEstimatedBalances(account.Id, account.StartingBalance);
                }

                _statusVM?.Report($"Synced {totalChanged} transaction update(s).", busy: false);
                return totalChanged;
            }
            catch (Exception ex)
            {
                _statusVM?.Report($"Sync failed: {ex.Message}", busy: false, StatusSeverity.Error);
                return 0;
            }
        }

        // DEV-ONLY, Sandbox-only: forces the current Item into
        // ITEM_LOGIN_REQUIRED via Plaid's sandbox-only simulation endpoint,
        // so the "Reconnect Bank" update-mode flow can be exercised
        // end-to-end without ever touching a real bank connection.
        public async Task<string> SimulateItemLoginRequiredAsync()
        {
            var environment = _settings.GetEnvironment();
            if (environment != PlaidEnvironmentOption.Sandbox)
                return "Refused: this simulation only runs against Sandbox.";

            var accessToken = _secretStores.AccessTokenStore.Get(PlaidLinkService.AccessTokenKey(environment));
            if (accessToken is null)
                return "No stored Sandbox access token to simulate against.";

            var client = _clientFactory.Create();
            var response = await client.SandboxItemResetLoginAsync(new Going.Plaid.Sandbox.SandboxItemResetLoginRequest
            {
                AccessToken = accessToken,
            });

            if (response.Error is not null)
                return $"Plaid error: {response.Error.ErrorCode} — {response.Error.ErrorMessage}";

            return response.ResetLogin
                ? "Item is now in ITEM_LOGIN_REQUIRED state. The next sync will detect it — try \"Reconnect Bank\"."
                : "Plaid did not confirm the reset.";
        }
    }
}
