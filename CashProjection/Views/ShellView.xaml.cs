using System.Windows;
using System.Windows.Input;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class ShellView : Window
    {
        public ShellView()
        {
            InitializeComponent();
            
            if (DataContext is ShellViewModel vm)
            {
                vm.SetViewReference(this);
                // Note: AccountVM.SetViewReference is set by AccountProjectionView itself
            }

            Closing += (s, e) =>
            {
                if (DataContext is ShellViewModel viewModel)
                {
                    e.Cancel = !viewModel.CanClose();
                }
            };
        }

        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.OnFindKeyDown(e);
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.ConfirmFind();
            }
        }
    }
}
