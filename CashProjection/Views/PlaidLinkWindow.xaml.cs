using System.Windows;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class PlaidLinkWindow : Window
    {
        private readonly PlaidLinkViewModel _vm;

        public PlaidLinkWindow(PlaidLinkViewModel vm)
        {
            InitializeComponent();

            _vm = vm;
            DataContext = vm;
            vm.RequestClose += (_, _) => Dispatcher.BeginInvoke(Close);

            Loaded += PlaidLinkWindow_Loaded;
            Closing += (_, _) => _vm.StopPolling();
        }

        private async void PlaidLinkWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await WebView.EnsureCoreWebView2Async();

            await _vm.InitializeAsync();

            if (_vm.HostedLinkUrl is not null)
                WebView.Source = new Uri(_vm.HostedLinkUrl);
        }
    }
}
