using CashProjection.Services.Secrets;

namespace CashProjection.Services.Plaid
{
    public sealed class PlaidClientFactory
    {
        private readonly SecretStoreFactory _secretStores;
        private readonly PlaidSettingsService _settings;

        public PlaidClientFactory(SecretStoreFactory secretStores, PlaidSettingsService settings)
        {
            _secretStores = secretStores;
            _settings = settings;
        }

        public static string ClientIdKey(PlaidEnvironmentOption env) =>
            env == PlaidEnvironmentOption.Production ? "Plaid:Production:ClientId" : "Plaid:ClientId";

        public static string SecretKey(PlaidEnvironmentOption env) =>
            env == PlaidEnvironmentOption.Production ? "Plaid:Production:Secret" : "Plaid:SandboxSecret";

        public Going.Plaid.PlaidClient Create()
        {
            var environment = _settings.GetEnvironment();
            var credStore = _secretStores.GetCredentialStore(environment);

            var clientId = credStore.Get(ClientIdKey(environment))
                ?? throw new InvalidOperationException(
                    $"No Plaid client_id configured for {environment}. " +
                    (environment == PlaidEnvironmentOption.Sandbox
                        ? "Create plaid.sandbox.secrets.json at the repo root with a ClientId field."
                        : "Store it in Windows Credential Manager first."));

            var secret = credStore.Get(SecretKey(environment))
                ?? throw new InvalidOperationException(
                    $"No Plaid secret configured for {environment}. " +
                    (environment == PlaidEnvironmentOption.Sandbox
                        ? "Create plaid.sandbox.secrets.json at the repo root with a SandboxSecret field."
                        : "Store it in Windows Credential Manager first."));

            var plaidEnvironment = environment == PlaidEnvironmentOption.Production
                ? Going.Plaid.Environment.Production
                : Going.Plaid.Environment.Sandbox;

            return new Going.Plaid.PlaidClient(plaidEnvironment, clientId: clientId, secret: secret);
        }
    }
}
