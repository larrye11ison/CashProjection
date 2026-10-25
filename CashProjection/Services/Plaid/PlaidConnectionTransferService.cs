using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CashProjection.Services.Secrets;

namespace CashProjection.Services.Plaid
{
    public sealed record PlaidConnectionPayload(
        PlaidEnvironmentOption Environment,
        string AccessToken,
        string ItemId,
        string ClientId,
        string Secret);

    // Lets a Plaid Item's access_token/item_id move to a second machine
    // without running Link again — which would create a brand-new Item and
    // burn one of a limited number of free Item slots (see issue #4).
    //
    // File layout: salt(16) + nonce(12) + tag(16) + ciphertext. AES-256-GCM
    // is authenticated, so a wrong passphrase on import fails the tag check
    // cleanly instead of silently "decrypting" to garbage that would get
    // written into Credential Manager as a bogus access token.
    public sealed class PlaidConnectionTransferService
    {
        private const int SaltSize = 16;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private const int Pbkdf2Iterations = 300_000;

        private readonly SecretStoreFactory _secretStores;

        public PlaidConnectionTransferService(SecretStoreFactory secretStores) => _secretStores = secretStores;

        public bool HasConnectionToExport(PlaidEnvironmentOption environment) =>
            _secretStores.AccessTokenStore.Get(PlaidLinkService.AccessTokenKey(environment)) is not null;

        public async Task ExportAsync(PlaidEnvironmentOption environment, string passphrase, string filePath)
        {
            var accessToken = _secretStores.AccessTokenStore.Get(PlaidLinkService.AccessTokenKey(environment))
                ?? throw new InvalidOperationException($"No stored {environment} connection to export.");
            var itemId = _secretStores.AccessTokenStore.Get(PlaidLinkService.ItemIdKey(environment)) ?? string.Empty;

            // Bundled alongside the access_token rather than left as a
            // separate per-machine setup step — they end up in the exact
            // same Credential Manager store either way, so splitting them
            // across two manual flows only added friction with no real
            // security benefit.
            var credStore = _secretStores.GetCredentialStore(environment);
            var clientId = credStore.Get(PlaidClientFactory.ClientIdKey(environment))
                ?? throw new InvalidOperationException($"No stored {environment} client_id to export — save Plaid client credentials first.");
            var secret = credStore.Get(PlaidClientFactory.SecretKey(environment))
                ?? throw new InvalidOperationException($"No stored {environment} secret to export — save Plaid client credentials first.");

            var plaintext = JsonSerializer.SerializeToUtf8Bytes(
                new PlaidConnectionPayload(environment, accessToken, itemId, clientId, secret));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var key = DeriveKey(passphrase, salt);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            using (var aesGcm = new AesGcm(key, TagSize))
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            var output = new byte[SaltSize + NonceSize + TagSize + ciphertext.Length];
            Buffer.BlockCopy(salt, 0, output, 0, SaltSize);
            Buffer.BlockCopy(nonce, 0, output, SaltSize, NonceSize);
            Buffer.BlockCopy(tag, 0, output, SaltSize + NonceSize, TagSize);
            Buffer.BlockCopy(ciphertext, 0, output, SaltSize + NonceSize + TagSize, ciphertext.Length);

            await File.WriteAllBytesAsync(filePath, output);
        }

        // Decrypts without writing anything to Credential Manager — the
        // caller decides whether/where to apply it (e.g. after confirming
        // an overwrite) via ApplyImportedConnection.
        public async Task<PlaidConnectionPayload> DecryptAsync(string filePath, string passphrase)
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            if (fileBytes.Length < SaltSize + NonceSize + TagSize)
                throw new InvalidOperationException("That file isn't a valid exported connection.");

            var salt = fileBytes[..SaltSize];
            var nonce = fileBytes[SaltSize..(SaltSize + NonceSize)];
            var tag = fileBytes[(SaltSize + NonceSize)..(SaltSize + NonceSize + TagSize)];
            var ciphertext = fileBytes[(SaltSize + NonceSize + TagSize)..];

            var key = DeriveKey(passphrase, salt);
            var plaintext = new byte[ciphertext.Length];

            try
            {
                using var aesGcm = new AesGcm(key, TagSize);
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("Incorrect passphrase, or the file is corrupted.");
            }

            try
            {
                return JsonSerializer.Deserialize<PlaidConnectionPayload>(plaintext)
                    ?? throw new InvalidOperationException("Incorrect passphrase, or the file is corrupted.");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Incorrect passphrase, or the file is corrupted.");
            }
        }

        public void ApplyImportedConnection(PlaidEnvironmentOption targetEnvironment, PlaidConnectionPayload payload)
        {
            _secretStores.AccessTokenStore.Set(PlaidLinkService.AccessTokenKey(targetEnvironment), payload.AccessToken);
            _secretStores.AccessTokenStore.Set(PlaidLinkService.ItemIdKey(targetEnvironment), payload.ItemId);

            // Sandbox client_id/secret live in the gitignored, manually-
            // maintained plaid.sandbox.secrets.json file (DevFileSecretStore),
            // which deliberately rejects writes — Sandbox is free/throwaway
            // and meant to be set up by hand per machine, so only
            // Production credentials get written here.
            if (targetEnvironment == PlaidEnvironmentOption.Production)
            {
                var credStore = _secretStores.GetCredentialStore(targetEnvironment);
                credStore.Set(PlaidClientFactory.ClientIdKey(targetEnvironment), payload.ClientId);
                credStore.Set(PlaidClientFactory.SecretKey(targetEnvironment), payload.Secret);
            }
        }

        private static byte[] DeriveKey(string passphrase, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
