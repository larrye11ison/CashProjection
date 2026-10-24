using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CashProjection.Data;
using CashProjection.Data.Repositories;
using CashProjection.Services;
using CashProjection.Services.Dialogs;
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
        private Window? _window;

        public App()
        {
            InitializeComponent();

            // Without this, any unhandled exception on the UI thread kills
            // the whole process with no trace — log it and let the user
            // decide whether to keep going, rather than crashing silently.
            UnhandledException += OnUnhandledException;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
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
                await linkWindow.ShowAndWaitAsync();

                if (!linkVm.Succeeded)
                {
                    // Connecting a bank account is mandatory for this app —
                    // there's nothing useful to show without it. Unlike WPF,
                    // WinUI doesn't exit the process just because a window
                    // closed, so this has to be explicit.
                    Exit();
                    return;
                }
            }

            var shellViewModel = _serviceProvider.GetRequiredService<ShellViewModel>();
            var shellView = _serviceProvider.GetRequiredService<ShellView>();
            _window = shellView;
            shellView.Closed += (_, _) => Exit();
            shellView.Activate();

            if (settings.GetNeedsReconnect())
            {
                shellViewModel.StatusVM.Report(
                    "Your bank connection needs to be reconnected — click \"Reconnect Bank\".",
                    busy: false,
                    StatusSeverity.Error);
            }

            // Kicks off the first sync immediately; ShellViewModel's own
            // timer takes over from here for periodic re-syncs.
            _ = shellViewModel.RunBackgroundSyncAsync();
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp_crash.txt"),
                    e.Exception.ToString());
            }
            catch { /* best-effort diagnostic only */ }

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
            services.AddSingleton<IDialogService, DialogService>();

            services.AddSingleton<StatusBarViewModel>();
            services.AddSingleton<AccountProjectionViewModel>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ShellView>();
            services.AddTransient<PlaidLinkViewModel>();
        }
    }
}
