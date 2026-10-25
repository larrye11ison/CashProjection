using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CashProjection.Services.Dialogs;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class PlaidEnvironmentSettingsDialog : ContentDialog
    {
        private const string ConnectionFileExtension = ".cpplaid";

        public PlaidEnvironmentSettingsViewModel ViewModel { get; }

        public PlaidEnvironmentSettingsDialog(PlaidEnvironmentSettingsViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();
            CloseButtonClick += (_, _) => ViewModel.CloseCommand.Execute(null);

            // Content/dialog size aren't final until first layout pass —
            // ScrollableHeight reads 0 before that — so this also needs a
            // check once Loaded, not just on ViewChanged (which only fires
            // from here on, after an actual scroll/resize).
            ContentScrollViewer.Loaded += (_, _) => UpdateMoreContentHint();
            ContentScrollViewer.SizeChanged += (_, _) => UpdateMoreContentHint();
        }

        private void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
            UpdateMoreContentHint();

        private void UpdateMoreContentHint()
        {
            var hasMoreBelow = ContentScrollViewer.ScrollableHeight - ContentScrollViewer.VerticalOffset > 1;
            MoreContentHint.Visibility = hasMoreBelow ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SecretPasswordBox_PasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.ProductionSecret = SecretPasswordBox.Password;

        private void SaveCredentialsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.SaveProductionCredentialsCommand.Execute(null);

        private void ExportConnectionButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.RequestExportConnectionCommand.Execute(null);

        private void ImportConnectionButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.RequestImportConnectionCommand.Execute(null);

        private void ExportPassphraseBox_PasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.TransferPassphrase = ExportPassphraseBox.Password;

        private void ImportPassphraseBox_PasswordChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
            ViewModel.TransferPassphrase = ImportPassphraseBox.Password;

        // FileSavePicker needs a real HWND to anchor to — the dialog's own
        // XamlRoot isn't enough — so this reaches for the main window
        // DialogHost already tracks (see ShellView's constructor).
        private async void ExportConnectionContinueButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.TransferPassphrase))
            {
                ViewModel.TransferValidationMessage = "Enter a passphrase first.";
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedFileName = $"CashProjection-Plaid-{ViewModel.CurrentEnvironment}",
            };
            picker.FileTypeChoices.Add("Encrypted Plaid connection", new List<string> { ConnectionFileExtension });
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(DialogHost.MainWindow));

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            await ViewModel.ExportConnectionAsync(file.Path);
        }

        private async void ImportConnectionContinueButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.TransferPassphrase))
            {
                ViewModel.TransferValidationMessage = "Enter a passphrase first.";
                return;
            }

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(ConnectionFileExtension);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(DialogHost.MainWindow));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await ViewModel.BeginImportConnectionAsync(file.Path);
        }
    }
}
