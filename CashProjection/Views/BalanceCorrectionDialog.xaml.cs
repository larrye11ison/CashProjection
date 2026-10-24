using Microsoft.UI.Xaml.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class BalanceCorrectionDialog : ContentDialog
    {
        public BalanceCorrectionViewModel ViewModel { get; }

        public BalanceCorrectionDialog(BalanceCorrectionViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();
            ViewModel.RequestClose += Hide;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.SaveCommand.Execute(null);
        }

        private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.CancelCommand.Execute(null);
        }
    }
}
