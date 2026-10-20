using System.Windows;
using System.Windows.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class MatchReviewView : UserControl
    {
        public MatchReviewView()
        {
            InitializeComponent();
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PresumptiveMatchRow row && DataContext is MatchReviewViewModel vm)
                vm.AcceptCommand.Execute(row);
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is PresumptiveMatchRow row && DataContext is MatchReviewViewModel vm)
                vm.RejectCommand.Execute(row);
        }
    }
}
