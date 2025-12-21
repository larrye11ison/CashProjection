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
    }
}