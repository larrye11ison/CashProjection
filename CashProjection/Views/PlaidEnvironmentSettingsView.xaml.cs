using System.Windows;
using System.Windows.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class PlaidEnvironmentSettingsView : UserControl
    {
        public PlaidEnvironmentSettingsView()
        {
            InitializeComponent();
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
