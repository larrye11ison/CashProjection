using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using CashProjection.Data;
using CashProjection.Data.Repositories;
using CashProjection.Models;
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
                // Offer Import before ever touching PlaidLinkWindow (and the
                // WebView2 control it depends on) — running Link
                // unconditionally here is exactly the new-Item-slot-burning
                // problem issue #4's export/import exists to avoid, and
                // would make Import unreachable on a second machine that
                // already has an exported connection waiting.
                var firstLaunchVm = _serviceProvider.GetRequiredService<PlaidFirstLaunchViewModel>();
                var firstLaunchWindow = new PlaidFirstLaunchWindow(firstLaunchVm);
                await firstLaunchWindow.ShowAndWaitAsync();

                if (firstLaunchVm.UserChoseConnectNew)
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
                else if (!firstLaunchVm.ImportSucceeded)
                {
                    // User closed the window without choosing either path.
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
                // XamlParseException's own Message is a hardcoded "XAML
                // parsing failed." regardless of cause — but the real
                // native HRESULT that triggered it survives in HResult
                // (set by WinRT.ExceptionHelpers.ThrowExceptionForHR), and
                // e.Message is a separate field the runtime sometimes
                // populates with more detail than Exception.Message. Plain
                // ToString() surfaces neither, so both are appended here.
                var details = $"{e.Exception}\n\n" +
                    $"HResult: 0x{e.Exception.HResult:X8}\n" +
                    $"UnhandledExceptionEventArgs.Message: {e.Message}";

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cp_crash.txt"),
                    details);
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
            services.AddSingleton<PlaidConnectionTransferService>();
            services.AddSingleton<PlaidSyncService>();
            services.AddSingleton<UnseenTransactionsTracker>();
            services.AddSingleton<FuzzyMatchingService>();
            services.AddSingleton<ChangeHistoryService>();
            services.AddSingleton<IDialogService, DialogService>();

            services.AddSingleton<StatusBarViewModel>();

            // AccountProjectionViewModel is now per-account (any account
            // can have projections, not just one DI-singleton-bound one),
            // so it's built on demand via this factory — one extra
            // runtime-supplied Account parameter alongside the rest, which
            // ActivatorUtilities resolves from the container as usual.
            services.AddSingleton<Func<Account, AccountProjectionViewModel>>(sp =>
                account => ActivatorUtilities.CreateInstance<AccountProjectionViewModel>(sp, account));

            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ShellView>();
            services.AddTransient<PlaidLinkViewModel>();
            services.AddTransient<PlaidFirstLaunchViewModel>();
        }
    }
}
