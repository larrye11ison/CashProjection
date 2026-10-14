using System.Windows;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class PlaidEnvironmentSettingsView : Window
    {
        public PlaidEnvironmentSettingsView(PlaidEnvironmentSettingsViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        // PasswordBox.Password is deliberately not a bindable dependency
        // property (security precaution against data-binding leaking
        // passwords into memory snapshots/visual tree) — wire it manually.
        private void SecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is PlaidEnvironmentSettingsViewModel vm)
                vm.ProductionSecret = SecretPasswordBox.Password;
        }
    }
}
