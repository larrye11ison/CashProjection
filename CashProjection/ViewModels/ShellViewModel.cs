using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Data.Repositories;
using CashProjection.Models;
using CashProjection.Services;
using CashProjection.Services.Matching;
using CashProjection.Services.Plaid;
using CashProjection.Services.Secrets;
using CashProjection.Views;

namespace CashProjection.ViewModels
{
    public sealed partial class ShellViewModel : ObservableObject
    {
        private readonly ChangeHistoryService _changeHistory;
        private readonly MatchRepository _matchRepo;
        private readonly ProjectionItemRepository _itemRepo;
        private readonly BankTransactionRepository _bankRepo;
        private readonly FuzzyMatchingService _matchingService;
        private readonly PlaidLinkService _linkService;
        private readonly PlaidSyncService _syncService;
        private readonly AccountRepository _accountRepo;
        private readonly PlaidSettingsService _plaidSettings;
        private readonly SecretStoreFactory _secretStores;

        private WeakReference<FrameworkElement>? _viewRef;

        [ObservableProperty]
        private bool _isFindOpen;

        [ObservableProperty]
        private string _findText = string.Empty;

        [ObservableProperty]
        private SearchResult? _selectedSearchResult;

        [ObservableProperty]
        private Account? _selectedAccount;

        [ObservableProperty]
        private AccountTransactionsViewModel? _otherAccountVM;

        public ShellViewModel(
            AccountProjectionViewModel accountVM,
            StatusBarViewModel statusVM,
            ChangeHistoryService changeHistory,
            MatchRepository matchRepo,
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            FuzzyMatchingService matchingService,
            PlaidLinkService linkService,
            PlaidSyncService syncService,
            AccountRepository accountRepo,
            PlaidSettingsService plaidSettings,
            SecretStoreFactory secretStores)
        {
            StatusVM = statusVM;
            AccountVM = accountVM;
            _changeHistory = changeHistory;
            _matchRepo = matchRepo;
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _matchingService = matchingService;
            _plaidSettings = plaidSettings;
            _secretStores = secretStores;
            _linkService = linkService;
            _syncService = syncService;
            _accountRepo = accountRepo;
            SearchResults = new ObservableCollection<SearchResult>();
            Accounts = new ObservableCollection<Account>(accountRepo.GetAll());
            SelectedAccount = Accounts.FirstOrDefault(a => a.SupportsProjections) ?? Accounts.FirstOrDefault();
            StatusVM.Report("Ready");
        }

        public AccountProjectionViewModel AccountVM { get; }

        public StatusBarViewModel StatusVM { get; }

        public ObservableCollection<SearchResult> SearchResults { get; }

        public ObservableCollection<Account> Accounts { get; }

        // Checking is the one account with projections/matching — the
        // toolbar's editing actions and the grid only make sense there.
        // Every other linked account (money market, savings, a CD, a line
        // of credit, ...) is balance-and-history-only.
        public bool IsCheckingSelected => SelectedAccount?.SupportsProjections == true;

        public bool IsOtherAccountSelected => SelectedAccount is not null && !IsCheckingSelected;

        partial void OnSelectedAccountChanged(Account? value)
        {
            OnPropertyChanged(nameof(IsCheckingSelected));
            OnPropertyChanged(nameof(IsOtherAccountSelected));

            OtherAccountVM = value is not null && !value.SupportsProjections
                ? new AccountTransactionsViewModel(value, _bankRepo)
                : null;
        }

        // Called after a sync completes, so whichever non-checking account
        // is currently on screen picks up fresh balance/transaction data
        // without the user having to re-select it.
        public void RefreshOtherAccountView() => OtherAccountVM?.Refresh();

        partial void OnFindTextChanged(string value)
        {
            UpdateSearchResults();
        }

        public void SetViewReference(FrameworkElement view)
        {
            _viewRef = new WeakReference<FrameworkElement>(view);
        }

        public bool CanClose()
        {
            AccountVM?.CommitPendingEdits();

            if (AccountVM?.IsDirty == true)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Save before exiting?",
                    "Cash Projection",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Cancel)
                    return false;

                if (result == MessageBoxResult.Yes)
                    AccountVM.Save();
            }

            return true;
        }

        [RelayCommand]
        public void AddNew() => AccountVM?.AddNew();

        [RelayCommand]
        public void Save() => AccountVM.Save();

        [RelayCommand]
        public void Undo() => AccountVM.UndoCommand.Execute(null);

        [RelayCommand]
        public void Redo() => AccountVM.RedoCommand.Execute(null);

        [RelayCommand]
        public void OpenHistory()
        {
            var vm = new ChangeHistoryViewModel(_changeHistory, AccountVM.ReloadTransactionsFromDatabase);
            var window = new ChangeHistoryView(vm) { Owner = Application.Current?.MainWindow };
            window.ShowDialog();
        }

        [RelayCommand]
        public void OpenPlaidSettings()
        {
            var vm = new PlaidEnvironmentSettingsViewModel(_plaidSettings, _secretStores, _linkService);
            var window = new PlaidEnvironmentSettingsView(vm) { Owner = Application.Current?.MainWindow };
            window.ShowDialog();
        }

        [RelayCommand]
        public void OpenMatches()
        {
            // Re-scan before showing results — matching otherwise only ever
            // ran once, right after the Plaid sync at startup, so items
            // added or edited mid-session were never considered.
            StatusVM.Report("Scanning for presumptive matches...", busy: true);
            var found = _matchingService.FindAndRecordPresumptiveMatches(AccountVM.AccountId);
            StatusVM.Report(
                found.Count > 0 ? $"Found {found.Count} new presumptive match(es)." : "No new presumptive matches found.",
                busy: false);

            var vm = new MatchReviewViewModel(_matchRepo, _itemRepo, _bankRepo, _changeHistory, AccountVM.ReloadTransactionsFromDatabase);
            var window = new MatchReviewView(vm) { Owner = Application.Current?.MainWindow };
            window.ShowDialog();
        }

        // Update-mode reconnect: repairs the existing Plaid Item in place
        // (no new Item, no new access_token, no Trial-plan slot consumed).
        // Available any time — not just when a sync has flagged a problem —
        // so a stale connection can be fixed proactively too.
        [RelayCommand]
        public void ReconnectBank()
        {
            var linkVm = new PlaidLinkViewModel(_linkService, isUpdateMode: true);
            var window = new PlaidLinkWindow(linkVm) { Owner = Application.Current?.MainWindow };
            window.ShowDialog();

            if (!linkVm.Succeeded)
                return;

            StatusVM.Report("Bank connection restored — syncing...", busy: true);
            var account = _accountRepo.GetOrCreateActiveAccount();
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await _syncService.SyncAsync(_accountRepo.GetAll());
                _matchingService.FindAndRecordPresumptiveMatches(account.Id);
                await Application.Current!.Dispatcher.InvokeAsync(() =>
                {
                    AccountVM.RefreshAccountFromDatabase();
                    AccountVM.ReloadTransactionsFromDatabase();
                    RefreshOtherAccountView();
                });
            });
        }

#if DEBUG
        public bool IsDebugBuild => true;

        // DEV-ONLY: forces the Sandbox Item into ITEM_LOGIN_REQUIRED (via
        // Plaid's sandbox-only simulation endpoint) and immediately syncs,
        // so the "Reconnect Bank" update-mode flow can be proven out safely
        // before ever touching a real bank connection.
        [RelayCommand]
        public async System.Threading.Tasks.Task SimulateReconnectNeeded()
        {
            StatusVM.Report("Simulating a broken bank connection (Sandbox only)...", busy: true);
            var result = await _syncService.SimulateItemLoginRequiredAsync();
            StatusVM.Report(result, busy: false);

            await _syncService.SyncAsync(_accountRepo.GetAll());
        }

        [RelayCommand]
        public void RunMatchingTest()
        {
            const string csvPath = @"C:\Users\pj\Downloads\statement_Chevron_Federal_Credit_Union_0900_07_01_2026_to_10_11_2026.csv";
            if (!System.IO.File.Exists(csvPath))
            {
                MessageBox.Show($"CSV not found at:\n{csvPath}", "Matching Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = Services.DevTools.ChevronCsvTestImporter.Load(csvPath);
            var lines = new System.Collections.Generic.List<string>();
            var itemsWithNoDateOverlap = 0;
            var itemsWithNoAmountSignMatch = 0;

            foreach (var item in AccountVM.Transactions)
            {
                var matchText = string.IsNullOrWhiteSpace(item.MatchText) ? item.Name : item.MatchText;
                var isPayment = item.Payment.HasValue;

                var withinDateWindow = rows.Where(r => (r.Date.Date - item.TransactionDate.Date).Duration().TotalDays <= 10).ToList();
                if (withinDateWindow.Count == 0)
                {
                    itemsWithNoDateOverlap++;
                    continue;
                }

                var sameDirection = withinDateWindow.Where(r => (r.Amount > 0) == isPayment).ToList();
                if (sameDirection.Count == 0)
                {
                    itemsWithNoAmountSignMatch++;
                    continue;
                }

                var best = sameDirection
                    .Select(r => new { Row = r, Score = FuzzySharp.Fuzz.PartialTokenSetRatio(matchText, r.Description) })
                    .OrderByDescending(x => x.Score)
                    .First();

                lines.Add($"{item.Name} ({item.TransactionDate:d}, {(isPayment ? "payment" : "deposit")}) -> {best.Row.Description} ({best.Row.Date:d})  score={best.Score}");
            }

            var summary = $"Loaded {rows.Count} CSV rows and {AccountVM.Transactions.Count} projection item(s).\n" +
                $"{itemsWithNoDateOverlap} item(s) had no CSV row within ±10 days.\n" +
                $"{itemsWithNoAmountSignMatch} item(s) had date overlap but no row matching payment/deposit direction.\n\n";

            var message = summary + (lines.Count > 0 ? string.Join("\n", lines) : "No projection item cleared both the date and amount-direction filters.");
            MessageBox.Show(message, $"Matching test — {rows.Count} CSV rows loaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
#else
        public bool IsDebugBuild => false;
#endif

        [RelayCommand]
        public void OpenFind()
        {
            IsFindOpen = true;
            FindText = string.Empty;
            SearchResults.Clear();
            SelectedSearchResult = null;

            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(FocusFindTextBox)
            );
        }

        [RelayCommand]
        public void CancelFind()
        {
            IsFindOpen = false;
        }

        [RelayCommand]
        public void ConfirmFind()
        {
            var target = SelectedSearchResult?.Item ?? SearchResults.FirstOrDefault()?.Item;
            if (target is null)
                return;

            IsFindOpen = false;
            AccountVM.FocusTransaction(target);
        }

        public void OnFindKeyDown(KeyEventArgs e)
        {
            if (!IsFindOpen)
                return;

            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    ConfirmFind();
                    break;
                case Key.Escape:
                    e.Handled = true;
                    CancelFind();
                    break;
                case Key.Down:
                    e.Handled = true;
                    MoveSelection(+1);
                    break;
                case Key.Up:
                    e.Handled = true;
                    MoveSelection(-1);
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            if (SearchResults.Count == 0)
                return;

            var idx = SelectedSearchResult is null
                ? -1
                : SearchResults.IndexOf(SelectedSearchResult);
            idx = Math.Clamp(idx + delta, 0, SearchResults.Count - 1);
            SelectedSearchResult = SearchResults[idx];
        }

        private void UpdateSearchResults()
        {
            var tokens = Regex
                .Split(FindText ?? string.Empty, @"[^0-9A-Za-z]+")
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Select(t => t.ToLowerInvariant())
                .Distinct()
                .ToArray();

            SearchResults.Clear();
            SelectedSearchResult = null;

            if (tokens.Length == 0 || AccountVM?.Transactions is null)
                return;

            var results = AccountVM
                .Transactions.Select(vm =>
                {
                    var lower = (vm.Name ?? string.Empty).ToLowerInvariant();
                    var hits = tokens.Count(tok => lower.Contains(tok));
                    return new { vm, hits };
                })
                .Where(x => x.hits > 0)
                .OrderByDescending(x => x.hits)
                .ThenBy(x => x.vm.TransactionDate)
                .ThenBy(x => x.vm.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new SearchResult(x.vm, x.hits));

            foreach (var r in results)
                SearchResults.Add(r);

            if (SearchResults.Count > 0)
                SelectedSearchResult = SearchResults[0];
        }

        private void FocusFindTextBox()
        {
            if (_viewRef != null && _viewRef.TryGetTarget(out var fe))
            {
                if (fe.FindName("FindTextBox") is TextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                    Keyboard.Focus(tb);
                }
            }
        }

        public sealed class SearchResult
        {
            public SearchResult(ProjectionItemViewModel item, int hits)
            {
                Item = item;
                Hits = hits;
                Display = $"{item.TransactionDate:d}  �  {item.Name}";
            }

            public ProjectionItemViewModel Item { get; }
            public int Hits { get; }
            public string Display { get; }
        }
    }
}
