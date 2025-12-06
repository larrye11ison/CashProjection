using System;
using System.Collections.Generic;
using System.Windows;
using Caliburn.Micro;
using CashProjection.ViewModels;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace CashProjection
{
    public class Bootstrapper : BootstrapperBase
    {
        private SimpleContainer? _container;

        public Bootstrapper()
        {
            Initialize();
        }

        protected override void Configure()
        {
            _container = new SimpleContainer();

            _container.Singleton<IWindowManager, WindowManager>();
            _container.Singleton<IEventAggregator, EventAggregator>();

            // Register ViewModels
            _container.PerRequest<ShellViewModel>();
            _container.PerRequest<AccountProjectionViewModel>();
        }

        protected override object GetInstance(Type service, string key)
        {
            return _container!.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container!.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            _container!.BuildUp(instance);
        }

        protected override async void OnStartup(object sender, StartupEventArgs e)
        {
            // Apply OS theme before displaying the window
            ApplyOSTheme();

            await DisplayRootViewForAsync<ShellViewModel>();
        }

        private void ApplyOSTheme()
        {
            bool isSystemDarkMode = IsSystemDarkMode();

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(isSystemDarkMode ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }

        private bool IsSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
                );
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int i && i == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
