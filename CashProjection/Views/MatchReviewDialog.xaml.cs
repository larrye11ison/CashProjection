using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class MatchReviewDialog : ContentDialog
    {
        public MatchReviewViewModel ViewModel { get; }

        public MatchReviewDialog(MatchReviewViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();
            CloseButtonClick += (_, _) => ViewModel.CloseCommand.Execute(null);
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PresumptiveMatchRow row)
                ViewModel.AcceptCommand.Execute(row);
        }

        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is PresumptiveMatchRow row)
                ViewModel.RejectCommand.Execute(row);
        }
    }
}
