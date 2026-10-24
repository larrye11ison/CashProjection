using Microsoft.UI.Xaml;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class PlaidLinkWindow : Window
    {
        private readonly TaskCompletionSource _closedTcs = new();

        public PlaidLinkViewModel ViewModel { get; }

        public PlaidLinkWindow(PlaidLinkViewModel vm)
        {
            InitializeComponent();

            ViewModel = vm;
            Title = "Connect your bank account";
            WindowBackdropHelper.ApplyMica(this);

            vm.RequestClose += (_, _) => DispatcherQueue.TryEnqueue(Close);

            Activated += PlaidLinkWindow_Activated;
            Closed += (_, _) =>
            {
                ViewModel.StopPolling();
                _closedTcs.TrySetResult();
            };
        }

        // The window-closing wait this exposes is what callers (ShellViewModel.
        // ReconnectBank, App startup) await instead of the WPF-only
        // Window.ShowDialog() pattern — WinUI windows have no built-in modal
        // "block until closed" API.
        public Task ShowAndWaitAsync()
        {
            Activate();
            return _closedTcs.Task;
        }

        private bool _initialized;

        private async void PlaidLinkWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (_initialized)
                return;
            _initialized = true;

            await WebView.EnsureCoreWebView2Async();
            await ViewModel.InitializeAsync();

            if (ViewModel.HostedLinkUrl is not null)
                WebView.Source = new Uri(ViewModel.HostedLinkUrl);
        }
    }
}
