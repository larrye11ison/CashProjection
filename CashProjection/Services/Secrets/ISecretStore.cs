namespace CashProjection.Services.Secrets
{
    // Local, synchronous key/value secret storage. Implementations are
    // either a gitignored dev file (Sandbox client_id/secret only) or
    // Windows Credential Manager (everything else, including access tokens).
    public interface ISecretStore
    {
        string? Get(string key);
        void Set(string key, string value);
        void Remove(string key);
    }
}
