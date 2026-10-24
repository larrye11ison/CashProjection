using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class AccountTransactionsView : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel), typeof(AccountTransactionsViewModel), typeof(AccountTransactionsView),
            new PropertyMetadata(null));

        public AccountTransactionsView()
        {
            InitializeComponent();
        }

        public AccountTransactionsViewModel? ViewModel
        {
            get => (AccountTransactionsViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
    }
}
