using CashProjection.Services.Plaid;

namespace CashProjection.Services.Secrets
{
    // Decides WHERE a given piece of Plaid secret data lives. The decision
    // is driven only by the stored PlaidEnvironment setting — never by
    // #if DEBUG — so a real Link call against Production never happens by
    // accident, and so the access token survives a switch between Debug
    // and Release builds.
    public sealed class SecretStoreFactory
    {
        private readonly DevFileSecretStore _devFileStore = new();
        private readonly WindowsCredentialManagerSecretStore _credManagerStore = new();

        // client_id / secret used to authenticate Plaid API calls.
        public ISecretStore GetCredentialStore(PlaidEnvironmentOption environment) =>
            environment == PlaidEnvironmentOption.Sandbox ? _devFileStore : _credManagerStore;

        // The exchanged access_token (and item_id) always live in Windows
        // Credential Manager, in both Sandbox and Production, so the one
        // real Production Link call exercises the exact same storage path
        // already proven out in Sandbox.
        public ISecretStore AccessTokenStore => _credManagerStore;
    }
}
