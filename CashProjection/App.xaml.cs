using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using CashProjection.Data;
using CashProjection.Data.Repositories;
using CashProjection.Services;
using CashProjection.Services.Matching;
using CashProjection.Services.Plaid;
using CashProjection.Services.Secrets;
using CashProjection.ViewModels;
using CashProjection.Views;

namespace CashProjection
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Without this, any unhandled exception on the UI thread kills
            // the whole process with no trace — log it and let the user
            // decide whether to keep going, rather than crashing silently.
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            ApplyOSTheme();

            // Avoid an implicit shutdown if the mandatory Plaid Link window
            // (shown before ShellView) is the only open window and gets closed.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var linkService = _serviceProvider.GetRequiredService<PlaidLinkService>();
            var settings = _serviceProvider.GetRequiredService<PlaidSettingsService>();

            var environment = settings.GetEnvironment();
            if (!linkService.HasStoredAccessToken(environment))
            {
                var linkVm = _serviceProvider.GetRequiredService<PlaidLinkViewModel>();
                var linkWindow = new PlaidLinkWindow(linkVm);
                linkWindow.ShowDialog();

                if (!linkVm.Succeeded)
                {
                    // Connecting a bank account is mandatory for this app — there's
                    // nothing useful to show without it.
                    Shutdown();
                    return;
                }
            }

            var shellViewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
            var shellView = _serviceProvider.GetRequiredService<ShellView>();
            MainWindow = shellView;
            shellView.Show();

            if (settings.GetNeedsReconnect())
            {
                shellViewModel.StatusVM.Report(
                    "Your bank connection needs to be reconnected — click \"Reconnect Bank\".",
                    busy: false,
                    StatusSeverity.Error);
            }

            // Back to normal behavior now that the real main window is up.
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Kicks off the first sync immediately; ShellViewModel's own
            // timer takes over from here for periodic re-syncs.
            _ = shellViewModel.RunBackgroundSyncAsync();
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp_crash.txt"),
                    e.Exception.ToString());
            }
            catch { /* best-effort diagnostic only */ }

            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails were written to " +
                $"{System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp_crash.txt")}",
                "Cash Projection — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<CashProjectionDbContext>();
            services.AddSingleton<AccountRepository>();
            services.AddSingleton<ProjectionItemRepository>();
            services.AddSingleton<BankTransactionRepository>();
            services.AddSingleton<AppSettingsRepository>();
            services.AddSingleton<MatchRepository>();
            services.AddSingleton<ChangeHistoryRepository>();

            services.AddSingleton<PlaidSettingsService>();
            services.AddSingleton<SecretStoreFactory>();
            services.AddSingleton<PlaidClientFactory>();
            services.AddSingleton<PlaidLinkService>();
            services.AddSingleton<PlaidSyncService>();
            services.AddSingleton<UnseenTransactionsTracker>();
            services.AddSingleton<FuzzyMatchingService>();
            services.AddSingleton<ChangeHistoryService>();

            services.AddSingleton<StatusBarViewModel>();
            services.AddSingleton<AccountProjectionViewModel>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ShellView>();
            services.AddTransient<PlaidLinkViewModel>();
        }

        private static void ApplyOSTheme()
        {
            bool isSystemDarkMode = IsSystemDarkMode();

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(isSystemDarkMode ? BaseTheme.Dark : BaseTheme.Light);
            paletteHelper.SetTheme(theme);
        }

        private static bool IsSystemDarkMode()
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
