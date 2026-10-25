using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using CashProjection.Services.Plaid;

namespace CashProjection.ViewModels
{
    // Shown at startup instead of going straight into PlaidLinkWindow when no
    // access token is stored for the active environment. Running Link
    // unconditionally here would force a brand-new Plaid Item — burning one
    // of a limited number of free Item slots — on every machine a user sets
    // this app up on, even when they already have a connection exported from
    // another machine (see issue #4's whole point: moving the SAME Item
    // between machines without ever running Link twice).
    public sealed partial class PlaidFirstLaunchViewModel : ObservableObject
    {
        private readonly PlaidSettingsService _settings;
        private readonly PlaidConnectionTransferService _transferService;

        // Visibility properties, not bool + Converter={StaticResource ...}:
        // Window (this ViewModel's view) doesn't derive from FrameworkElement
        // in WinUI, and the x:Bind compiler's generated SetConverterLookupRoot
        // call requires one — StaticResource converters inside an x:Bind
        // simply fail to compile on a Window root (this is also why
        // PlaidLinkWindow's one x:Bind uses no converter). ContentDialogs
        // don't have this restriction since ContentDialog IS a
        // FrameworkElement.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChoiceVisibility))]
        [NotifyPropertyChangedFor(nameof(ImportPromptVisibility))]
        private bool _isShowingImportPrompt;

        public Visibility ChoiceVisibility => IsShowingImportPrompt ? Visibility.Collapsed : Visibility.Visible;

        public Visibility ImportPromptVisibility => IsShowingImportPrompt ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty]
        private string _passphrase = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ValidationMessageVisibility))]
        private string _validationMessage = string.Empty;

        public Visibility ValidationMessageVisibility =>
            string.IsNullOrEmpty(ValidationMessage) ? Visibility.Collapsed : Visibility.Visible;

        // Read by App.xaml.cs after the window closes to decide what to do
        // next — neither flag set means the user closed the window without
        // completing either path.
        public bool ImportSucceeded { get; private set; }

        public bool UserChoseConnectNew { get; private set; }

        public event Action? RequestClose;

        public PlaidFirstLaunchViewModel(PlaidSettingsService settings, PlaidConnectionTransferService transferService)
        {
            _settings = settings;
            _transferService = transferService;
        }

        [RelayCommand]
        public void ChooseConnectNew()
        {
            UserChoseConnectNew = true;
            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void ShowImportPrompt()
        {
            Passphrase = string.Empty;
            ValidationMessage = string.Empty;
            IsShowingImportPrompt = true;
        }

        [RelayCommand]
        public void CancelImportPrompt()
        {
            IsShowingImportPrompt = false;
            Passphrase = string.Empty;
            ValidationMessage = string.Empty;
        }

        // Called from code-behind once the user has entered a passphrase and
        // picked a file via FileOpenPicker.
        public async Task ImportFromFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(Passphrase))
            {
                ValidationMessage = "Enter a passphrase first.";
                return;
            }

            try
            {
                var payload = await _transferService.DecryptAsync(filePath, Passphrase);

                // The environment setting itself is synced via OneDrive
                // (AppSettingsRepository), so it should already match the
                // machine this was exported from — this is a defensive
                // check, not the primary way a user is expected to resolve
                // a mismatch (there's no environment switcher this early).
                var environment = _settings.GetEnvironment();
                if (payload.Environment != environment)
                {
                    ValidationMessage = $"That file holds a {payload.Environment} connection, but this app is currently set to {environment}.";
                    return;
                }

                _transferService.ApplyImportedConnection(environment, payload);
                ImportSucceeded = true;
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                ValidationMessage = ex.Message;
            }
            finally
            {
                Passphrase = string.Empty;
            }
        }
    }
}
