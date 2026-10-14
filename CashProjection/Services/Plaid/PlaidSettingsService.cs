using CashProjection.Data.Repositories;

namespace CashProjection.Services.Plaid
{
    // Reads/writes the handful of Plaid-related AppSettings rows. The
    // environment defensively defaults to Sandbox for ANY value other than
    // the literal string "Production" (missing row, null, corrupt data) —
    // a real Link call against Production must never happen by accident.
    public sealed class PlaidSettingsService
    {
        private const string EnvironmentKey = "PlaidEnvironment";
        private const string NeedsReconnectKeyPrefix = "PlaidNeedsReconnect";

        private readonly AppSettingsRepository _settings;

        public PlaidSettingsService(AppSettingsRepository settings) => _settings = settings;

        public PlaidEnvironmentOption GetEnvironment() =>
            _settings.Get(EnvironmentKey) == nameof(PlaidEnvironmentOption.Production)
                ? PlaidEnvironmentOption.Production
                : PlaidEnvironmentOption.Sandbox;

        public void SetEnvironment(PlaidEnvironmentOption environment) =>
            _settings.Set(EnvironmentKey, environment.ToString());

        // Scoped per environment — Sandbox and Production are different
        // Plaid Items with independent sync state. A cursor from one is
        // meaningless (and rejected by Plaid as malformed) when sent for
        // the other, which is exactly what happened the first time this
        // went un-scoped: switching to Production reused the leftover
        // Sandbox cursor and Plaid returned "cursor must be a properly
        // formatted string."
        public string? GetSyncCursor() => _settings.Get(SyncCursorKey(GetEnvironment()));

        public void SetSyncCursor(string cursor) => _settings.Set(SyncCursorKey(GetEnvironment()), cursor);

        // Likewise scoped per environment, so a reconnect-needed flag from
        // one environment can never bleed into the other.
        public bool GetNeedsReconnect() => _settings.Get(NeedsReconnectKey(GetEnvironment())) == "true";

        public void SetNeedsReconnect(bool value) => _settings.Set(NeedsReconnectKey(GetEnvironment()), value ? "true" : "false");

        private static string SyncCursorKey(PlaidEnvironmentOption env) => $"PlaidSyncCursor:{env}";

        private static string NeedsReconnectKey(PlaidEnvironmentOption env) => $"{NeedsReconnectKeyPrefix}:{env}";
    }
}
