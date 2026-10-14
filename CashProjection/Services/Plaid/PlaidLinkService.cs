using System.Linq;
using CashProjection.Services.Secrets;

namespace CashProjection.Services.Plaid
{
    public sealed record LinkInitResult(string LinkToken, string HostedLinkUrl);

    public sealed record LinkSessionStatus(bool IsFinished, string? PublicToken, string? ExitMessage);

    // Update mode never returns a public_token — the existing access_token
    // is simply repaired/revalidated in place — so completion is judged by
    // "finished with no exit error" rather than "a token was handed back."
    public sealed record UpdateModeSessionStatus(bool IsFinished, bool Succeeded, string? ExitMessage);

    public sealed class PlaidLinkService
    {
        private readonly PlaidClientFactory _clientFactory;
        private readonly SecretStoreFactory _secretStores;
        private readonly PlaidSettingsService _settings;

        public PlaidLinkService(PlaidClientFactory clientFactory, SecretStoreFactory secretStores, PlaidSettingsService settings)
        {
            _clientFactory = clientFactory;
            _secretStores = secretStores;
            _settings = settings;
        }

        public static string AccessTokenKey(PlaidEnvironmentOption env) => $"Plaid:{env}:AccessToken";

        public static string ItemIdKey(PlaidEnvironmentOption env) => $"Plaid:{env}:ItemId";

        public bool HasStoredAccessToken(PlaidEnvironmentOption environment) =>
            _secretStores.AccessTokenStore.Get(AccessTokenKey(environment)) is not null;

        // Hosted Link replaces Plaid's now-deprecated raw webview integration
        // (isWebview=true raised "deprecated" at runtime — see
        // https://plaid.com/docs/link/hosted-link/). The returned URL is a
        // normal web page suitable for any browser or webview control;
        // completion is detected by polling /link/token/get, not by
        // intercepting navigation or window.postMessage.
        public async Task<LinkInitResult> CreateLinkTokenAsync()
        {
            var client = _clientFactory.Create();

            var response = await client.LinkTokenCreateAsync(new Going.Plaid.Link.LinkTokenCreateRequest
            {
                ClientName = "Cash Projection",
                Language = Going.Plaid.Entity.Language.English,
                CountryCodes = new[] { Going.Plaid.Entity.CountryCode.Us },
                User = new Going.Plaid.Entity.LinkTokenCreateRequestUser
                {
                    ClientUserId = "cashprojection-local-user",
                },
                Products = new[] { Going.Plaid.Entity.Products.Transactions },
                HostedLink = new Going.Plaid.Entity.LinkTokenCreateHostedLink(),
                // Without this, Link authorizes every depository/loan
                // account the institution offers under one login (checking,
                // savings, money market, CDs, lines of credit, ...) with no
                // account-selection step at all for some institutions —
                // which is how unrelated accounts' activity ended up mixed
                // into this app's transaction feed the first time around.
                AccountFilters = new Going.Plaid.Entity.LinkTokenAccountFilters
                {
                    Depository = new Going.Plaid.Entity.DepositoryFilter
                    {
                        AccountSubtypes = new[] { Going.Plaid.Entity.DepositoryAccountSubtype.Checking },
                    },
                },
            });

            if (response.Error is not null)
                throw new InvalidOperationException($"Plaid error creating link token: {response.Error.ErrorCode} — {response.Error.ErrorMessage}");

            if (string.IsNullOrEmpty(response.HostedLinkUrl))
                throw new InvalidOperationException(
                    "Plaid did not return a Hosted Link URL. Hosted Link may need to be enabled for this account.");

            return new LinkInitResult(response.LinkToken, response.HostedLinkUrl);
        }

        // Update mode: pass the EXISTING access_token instead of Products/User.
        // Plaid repairs the same Item in place — no new Item is created, no
        // public_token is exchanged, and the stored access_token is unchanged.
        // This is the only safe way to handle "please reconnect your bank" on
        // a Trial plan, since creating a fresh Item here would permanently
        // burn one of the lifetime 10-Item slots for no reason.
        public async Task<LinkInitResult> CreateUpdateModeLinkTokenAsync()
        {
            var environment = _settings.GetEnvironment();
            var accessToken = _secretStores.AccessTokenStore.Get(AccessTokenKey(environment))
                ?? throw new InvalidOperationException("No stored access token to reconnect — run the initial Link flow first.");

            var client = _clientFactory.Create();

            var response = await client.LinkTokenCreateAsync(new Going.Plaid.Link.LinkTokenCreateRequest
            {
                ClientName = "Cash Projection",
                Language = Going.Plaid.Entity.Language.English,
                CountryCodes = new[] { Going.Plaid.Entity.CountryCode.Us },
                // user/user_id is required on every /link/token/create call,
                // update mode included — only Products gets omitted.
                User = new Going.Plaid.Entity.LinkTokenCreateRequestUser
                {
                    ClientUserId = "cashprojection-local-user",
                },
                AccessToken = accessToken,
                HostedLink = new Going.Plaid.Entity.LinkTokenCreateHostedLink(),
            });

            if (response.Error is not null)
                throw new InvalidOperationException($"Plaid error creating update-mode link token: {response.Error.ErrorCode} — {response.Error.ErrorMessage}");

            if (string.IsNullOrEmpty(response.HostedLinkUrl))
                throw new InvalidOperationException(
                    "Plaid did not return a Hosted Link URL for update mode. Hosted Link may need to be enabled for this account.");

            return new LinkInitResult(response.LinkToken, response.HostedLinkUrl);
        }

        public async Task<UpdateModeSessionStatus> PollUpdateModeSessionAsync(string linkToken)
        {
            var client = _clientFactory.Create();

            var response = await client.LinkTokenGetAsync(new Going.Plaid.Link.LinkTokenGetRequest
            {
                LinkToken = linkToken,
            });

            var session = response.LinkSessions?.FirstOrDefault(s => s.FinishedAt.HasValue);
            if (session is null)
                return new UpdateModeSessionStatus(false, false, null);

            if (session.Exit is not null)
            {
                var exitMessage = session.Exit.Error?.ErrorMessage ?? "Reconnect was closed before completing.";
                return new UpdateModeSessionStatus(true, false, exitMessage);
            }

            return new UpdateModeSessionStatus(true, true, null);
        }

        public async Task<LinkSessionStatus> PollSessionAsync(string linkToken)
        {
            var client = _clientFactory.Create();

            var response = await client.LinkTokenGetAsync(new Going.Plaid.Link.LinkTokenGetRequest
            {
                LinkToken = linkToken,
            });

            var session = response.LinkSessions?.FirstOrDefault(s => s.FinishedAt.HasValue);
            if (session is null)
                return new LinkSessionStatus(false, null, null);

            var publicToken = session.Results?.ItemAddResults?.FirstOrDefault()?.PublicToken;
            if (!string.IsNullOrEmpty(publicToken))
                return new LinkSessionStatus(true, publicToken, null);

            var exitMessage = session.Exit?.Error?.ErrorMessage ?? "Link was closed before completing.";
            return new LinkSessionStatus(true, null, exitMessage);
        }

        public async Task ExchangePublicTokenAsync(string publicToken)
        {
            var client = _clientFactory.Create();

            var response = await client.ItemPublicTokenExchangeAsync(new Going.Plaid.Item.ItemPublicTokenExchangeRequest
            {
                PublicToken = publicToken,
            });

            if (response.Error is not null)
                throw new InvalidOperationException($"Plaid error exchanging public token: {response.Error.ErrorCode} — {response.Error.ErrorMessage}");

            var environment = _settings.GetEnvironment();
            _secretStores.AccessTokenStore.Set(AccessTokenKey(environment), response.AccessToken);
            _secretStores.AccessTokenStore.Set(ItemIdKey(environment), response.ItemId);
        }
    }
}
