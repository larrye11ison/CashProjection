using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Services.Plaid;
using CashProjection.Services.Secrets;

namespace CashProjection.ViewModels
{
    public sealed partial class PlaidEnvironmentSettingsViewModel : ObservableObject
    {
        private readonly PlaidSettingsService _settings;
        private readonly SecretStoreFactory _secretStores;
        private readonly PlaidLinkService _linkService;

        [ObservableProperty]
        private PlaidEnvironmentOption _currentEnvironment;

        [ObservableProperty]
        private string _productionClientId = string.Empty;

        // Never prefilled from storage — only written when the user types a
        // new value, so a stored secret is never echoed back into the UI.
        [ObservableProperty]
        private string _productionSecret = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStoredProductionAccessTokenDisplay))]
        private bool _hasStoredProductionAccessToken;

        public string HasStoredProductionAccessTokenDisplay => $"Production access token stored: {HasStoredProductionAccessToken}";

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // Switching to Production confirms in-place (an overlay within this
        // same dialog) rather than via a second ContentDialog — see
        // ChangeHistoryViewModel's comment on PendingRollbackEntry for why.
        [ObservableProperty]
        private bool _isConfirmingSwitchToProduction;

        public string SwitchToProductionConfirmationMessage => HasStoredProductionAccessToken
            ? "Switch to Production using your already-connected bank account? No new Link will be performed."
            : "This will require a REAL bank Link the next time the app starts — a real connection to your actual bank account. Continue?";

        public event Action? RequestClose;

        public PlaidEnvironmentSettingsViewModel(PlaidSettingsService settings, SecretStoreFactory secretStores, PlaidLinkService linkService)
        {
            _settings = settings;
            _secretStores = secretStores;
            _linkService = linkService;

            CurrentEnvironment = _settings.GetEnvironment();
            HasStoredProductionAccessToken = _linkService.HasStoredAccessToken(PlaidEnvironmentOption.Production);

            var credStore = _secretStores.GetCredentialStore(PlaidEnvironmentOption.Production);
            ProductionClientId = credStore.Get(PlaidClientFactory.ClientIdKey(PlaidEnvironmentOption.Production)) ?? string.Empty;
        }

        [RelayCommand]
        public void SaveProductionCredentials()
        {
            if (string.IsNullOrWhiteSpace(ProductionClientId))
            {
                StatusMessage = "Enter a Production client_id first.";
                return;
            }

            var credStore = _secretStores.GetCredentialStore(PlaidEnvironmentOption.Production);
            credStore.Set(PlaidClientFactory.ClientIdKey(PlaidEnvironmentOption.Production), ProductionClientId.Trim());

            if (!string.IsNullOrWhiteSpace(ProductionSecret))
            {
                credStore.Set(PlaidClientFactory.SecretKey(PlaidEnvironmentOption.Production), ProductionSecret.Trim());
                ProductionSecret = string.Empty;
            }

            StatusMessage = "Production credentials saved to Windows Credential Manager.";
        }

        [RelayCommand]
        public void RequestSwitchToProduction()
        {
            var credStore = _secretStores.GetCredentialStore(PlaidEnvironmentOption.Production);
            var hasClientId = credStore.Get(PlaidClientFactory.ClientIdKey(PlaidEnvironmentOption.Production)) is not null;
            var hasSecret = credStore.Get(PlaidClientFactory.SecretKey(PlaidEnvironmentOption.Production)) is not null;
            if (!hasClientId || !hasSecret)
            {
                StatusMessage = "Save both a Production client_id and secret before switching.";
                return;
            }

            IsConfirmingSwitchToProduction = true;
        }

        [RelayCommand]
        public void CancelSwitchToProduction() => IsConfirmingSwitchToProduction = false;

        [RelayCommand]
        public void ConfirmSwitchToProduction()
        {
            IsConfirmingSwitchToProduction = false;

            _settings.SetEnvironment(PlaidEnvironmentOption.Production);
            CurrentEnvironment = PlaidEnvironmentOption.Production;
            StatusMessage = HasStoredProductionAccessToken
                ? "Switched to Production. Restart the app to use your existing connection."
                : "Switched to Production. Restart the app to connect your real bank account.";
        }

        [RelayCommand]
        public void SwitchToSandbox()
        {
            _settings.SetEnvironment(PlaidEnvironmentOption.Sandbox);
            CurrentEnvironment = PlaidEnvironmentOption.Sandbox;
            StatusMessage = "Switched back to Sandbox. Restart the app to use test data again. " +
                "Your Production connection, if any, is kept — not deleted.";
        }

        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
    }
}
