using System.Windows;
using System.Windows.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class AccountProjectionView : UserControl
    {
        public AccountProjectionView()
        {
            InitializeComponent();
            
            Loaded += (s, e) =>
            {
                if (DataContext is AccountProjectionViewModel vm)
                {
                    vm.SetViewReference(this);
                }
            };
        }

        private void PushForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && 
                button.Tag is TransactionItemViewModel transaction &&
                DataContext is AccountProjectionViewModel vm)
            {
                vm.PushForwardCommand.Execute(transaction);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && 
                button.Tag is TransactionItemViewModel transaction &&
                DataContext is AccountProjectionViewModel vm)
            {
                vm.DeleteTransactionCommand.Execute(transaction);
            }
        }
    }
}