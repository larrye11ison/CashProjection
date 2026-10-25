using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class PlaidFirstLaunchWindow : Window
    {
        private const string ConnectionFileExtension = ".cpplaid";

        private readonly TaskCompletionSource _closedTcs = new();

        public PlaidFirstLaunchViewModel ViewModel { get; }

        public PlaidFirstLaunchWindow(PlaidFirstLaunchViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();

            Title = "Connect your bank account";
            WindowBackdropHelper.ApplyMica(this);
            WindowIconHelper.SetIcon(this, "Assets\\AppIcon.ico");

            ViewModel.RequestClose += () => DispatcherQueue.TryEnqueue(Close);
            Closed += (_, _) => _closedTcs.TrySetResult();
        }

        // Same "no modal Window.ShowDialog() in WinUI" pattern as
        // PlaidLinkWindow — callers await this instead.
        public Task ShowAndWaitAsync()
        {
            Activate();
            return _closedTcs.Task;
        }

        private void ConnectNewButton_Click(object sender, RoutedEventArgs e) =>
            ViewModel.ChooseConnectNewCommand.Execute(null);

        private void ImportButton_Click(object sender, RoutedEventArgs e) =>
            ViewModel.ShowImportPromptCommand.Execute(null);

        private void PassphraseBox_PasswordChanged(object sender, RoutedEventArgs e) =>
            ViewModel.Passphrase = PassphraseBox.Password;

        private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.Passphrase))
            {
                ViewModel.ValidationMessage = "Enter a passphrase first.";
                return;
            }

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(ConnectionFileExtension);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await ViewModel.ImportFromFileAsync(file.Path);
        }
    }
}
