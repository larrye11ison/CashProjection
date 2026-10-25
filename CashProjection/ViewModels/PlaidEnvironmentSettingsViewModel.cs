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
        private readonly PlaidConnectionTransferService _transferService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanExportCurrentEnvironmentConnection))]
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

        // Export/Import move the CURRENT environment's connection — Sandbox
        // or Production, whichever is active — never both at once.
        [ObservableProperty]
        private bool _isExportingConnection;

        [ObservableProperty]
        private bool _isImportingConnection;

        // Shared by both the export and import passphrase overlays — they
        // never show at the same time, so one backing field is enough.
        [ObservableProperty]
        private string _transferPassphrase = string.Empty;

        // Validation errors specific to the passphrase overlays (e.g. "enter
        // a passphrase first") must render INSIDE the overlay — setting
        // StatusMessage for these would write to the TextBlock on the main
        // dialog page, which sits entirely behind the still-open overlay and
        // is invisible.
        [ObservableProperty]
        private string _transferValidationMessage = string.Empty;

        [ObservableProperty]
        private bool _isConfirmingImportOverwrite;

        private PlaidConnectionPayload? _pendingImportPayload;

        public bool CanExportCurrentEnvironmentConnection => _transferService.HasConnectionToExport(CurrentEnvironment);

        public string ImportOverwriteConfirmationMessage =>
            $"This will overwrite the existing stored {CurrentEnvironment} connection — including client credentials — " +
            "with the one from the imported file. This cannot be undone. Continue?";

        public event Action? RequestClose;

        public PlaidEnvironmentSettingsViewModel(
            PlaidSettingsService settings,
            SecretStoreFactory secretStores,
            PlaidLinkService linkService,
            PlaidConnectionTransferService transferService)
        {
            _settings = settings;
            _secretStores = secretStores;
            _linkService = linkService;
            _transferService = transferService;

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
        public void RequestExportConnection()
        {
            TransferPassphrase = string.Empty;
            TransferValidationMessage = string.Empty;
            IsExportingConnection = true;
        }

        [RelayCommand]
        public void CancelExportConnection()
        {
            IsExportingConnection = false;
            TransferPassphrase = string.Empty;
            TransferValidationMessage = string.Empty;
        }

        // Called from code-behind once the user has entered a passphrase
        // and picked a save location via FileSavePicker.
        public async Task ExportConnectionAsync(string filePath)
        {
            if (string.IsNullOrEmpty(TransferPassphrase))
            {
                TransferValidationMessage = "Enter a passphrase first.";
                return;
            }

            try
            {
                await _transferService.ExportAsync(CurrentEnvironment, TransferPassphrase, filePath);
                StatusMessage = $"Exported the {CurrentEnvironment} connection, including client credentials. " +
                    "Keep the passphrase safe — it's required to import it.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
            finally
            {
                IsExportingConnection = false;
                TransferPassphrase = string.Empty;
            }
        }

        [RelayCommand]
        public void RequestImportConnection()
        {
            TransferPassphrase = string.Empty;
            TransferValidationMessage = string.Empty;
            IsImportingConnection = true;
        }

        [RelayCommand]
        public void CancelImportConnection()
        {
            IsImportingConnection = false;
            TransferPassphrase = string.Empty;
            TransferValidationMessage = string.Empty;
        }

        // Called from code-behind once the user has entered a passphrase
        // and picked a file via FileOpenPicker. Decrypts only — nothing is
        // written to Credential Manager yet, since an existing stored
        // connection for this environment needs an explicit overwrite
        // confirmation first (see ConfirmImportOverwrite).
        public async Task BeginImportConnectionAsync(string filePath)
        {
            if (string.IsNullOrEmpty(TransferPassphrase))
            {
                TransferValidationMessage = "Enter a passphrase first.";
                return;
            }

            try
            {
                var payload = await _transferService.DecryptAsync(filePath, TransferPassphrase);

                // The access_token key is environment-scoped — importing a
                // Sandbox export while Production is active (or vice versa)
                // would silently write the wrong environment's token under
                // the active environment's key.
                if (payload.Environment != CurrentEnvironment)
                {
                    StatusMessage = $"That file holds a {payload.Environment} connection, but {CurrentEnvironment} is currently active. " +
                        "Switch environment first, then import.";
                    IsImportingConnection = false;
                    return;
                }

                IsImportingConnection = false;

                if (_linkService.HasStoredAccessToken(CurrentEnvironment))
                {
                    _pendingImportPayload = payload;
                    IsConfirmingImportOverwrite = true;
                }
                else
                {
                    ApplyImport(payload);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
                IsImportingConnection = false;
            }
            finally
            {
                TransferPassphrase = string.Empty;
            }
        }

        [RelayCommand]
        public void ConfirmImportOverwrite()
        {
            IsConfirmingImportOverwrite = false;
            if (_pendingImportPayload is { } payload)
                ApplyImport(payload);
            _pendingImportPayload = null;
        }

        [RelayCommand]
        public void CancelImportOverwrite()
        {
            IsConfirmingImportOverwrite = false;
            _pendingImportPayload = null;
            StatusMessage = "Import cancelled.";
        }

        private void ApplyImport(PlaidConnectionPayload payload)
        {
            _transferService.ApplyImportedConnection(CurrentEnvironment, payload);
            OnPropertyChanged(nameof(CanExportCurrentEnvironmentConnection));

            if (CurrentEnvironment == PlaidEnvironmentOption.Production)
            {
                HasStoredProductionAccessToken = true;
                ProductionClientId = payload.ClientId;
                StatusMessage = $"Imported the {CurrentEnvironment} connection, including client credentials. Restart the app to use it — " +
                    "and make sure no other machine is using this same connection at the same time.";
            }
            else
            {
                // DevFileSecretStore (plaid.sandbox.secrets.json) rejects
                // writes by design — see ApplyImportedConnection — so the
                // exported client_id/secret never actually land anywhere
                // for Sandbox, unlike Production.
                StatusMessage = $"Imported the {CurrentEnvironment} connection. Restart the app to use it — " +
                    "and make sure no other machine is using this same connection at the same time. " +
                    "Sandbox client credentials still need to be set up manually (plaid.sandbox.secrets.json), since that file is never written by the app.";
            }
        }

        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
    }
}
