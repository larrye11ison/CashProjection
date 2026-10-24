using Microsoft.UI.Xaml.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class PlaidEnvironmentSettingsDialog : ContentDialog
    {
        public PlaidEnvironmentSettingsViewModel ViewModel { get; }

        public PlaidEnvironmentSettingsDialog(PlaidEnvironmentSettingsViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();
            CloseButtonClick += (_, _) => ViewModel.CloseCommand.Execute(null);
        }

        private void SecretPasswordBox_PasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.ProductionSecret = SecretPasswordBox.Password;

        private void SaveCredentialsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.SaveProductionCredentialsCommand.Execute(null);
    }
}
