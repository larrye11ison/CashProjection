using System.Windows;

namespace CashProjection
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly Bootstrapper _bootstrapper;

        public App()
        {
            InitializeComponent();
            _bootstrapper = new Bootstrapper();
        }
    }
}
