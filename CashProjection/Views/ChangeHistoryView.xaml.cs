using System.Windows;
using System.Windows.Controls;
using CashProjection.Models;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class ChangeHistoryView : Window
    {
        public ChangeHistoryView(ChangeHistoryViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ChangeHistoryEntry entry && DataContext is ChangeHistoryViewModel vm)
                vm.UndoCommand.Execute(entry);
        }

        private void RollbackButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ChangeHistoryEntry entry && DataContext is ChangeHistoryViewModel vm)
                vm.RollbackToHereCommand.Execute(entry);
        }
    }
}
