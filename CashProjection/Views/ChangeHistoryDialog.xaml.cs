using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CashProjection.Models;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class ChangeHistoryDialog : ContentDialog
    {
        public ChangeHistoryViewModel ViewModel { get; }

        public ChangeHistoryDialog(ChangeHistoryViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();
            CloseButtonClick += (_, _) => ViewModel.CloseCommand.Execute(null);
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChangeHistoryEntry entry)
                ViewModel.UndoCommand.Execute(entry);
        }

        // Rollback confirms in-place within this same dialog now — see
        // ChangeHistoryViewModel's comment on PendingRollbackEntry — so
        // this just arms that overlay rather than awaiting a second
        // ContentDialog.
        private void RollbackButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ChangeHistoryEntry entry)
                ViewModel.RequestRollbackCommand.Execute(entry);
        }
    }
}
