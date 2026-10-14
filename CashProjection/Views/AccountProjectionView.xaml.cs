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
                button.Tag is ProjectionItemViewModel transaction &&
                DataContext is AccountProjectionViewModel vm)
            {
                vm.PushForwardCommand.Execute(transaction);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is ProjectionItemViewModel transaction &&
                DataContext is AccountProjectionViewModel vm)
            {
                vm.DeleteTransactionCommand.Execute(transaction);
            }
        }

        // Real bank-transaction rows are read-only facts from Plaid — block
        // editing on any column, regardless of which cell was double-clicked.
        private void TransactionsGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is ProjectionItemViewModel { IsReadOnly: true })
                e.Cancel = true;
        }
    }
}