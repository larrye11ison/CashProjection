using System;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
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
        private readonly UnseenTransactionsTracker _unseenTracker;
        private readonly Func<Account, AccountProjectionViewModel> _accountVMFactory;
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        private readonly DispatcherTimer _syncTimer;

        // Guards RunBackgroundSyncAsync against overlap: the periodic timer,
        // the startup sync, and a manual "Reconnect Bank" can all try to
        // trigger one around the same time, and Plaid's sync cursor isn't
        // safe to walk concurrently from two callers at once. 0 = idle,
        // 1 = a sync is in flight.
        private int _isSyncing;

        [ObservableProperty]
        private bool _isFindOpen;

        [ObservableProperty]
        private bool _isChangeHistoryOpen;

        [ObservableProperty]
        private ChangeHistoryViewModel? _changeHistoryVM;

        [ObservableProperty]
        private bool _isMatchReviewOpen;

        [ObservableProperty]
        private MatchReviewViewModel? _matchReviewVM;

        [ObservableProperty]
        private bool _isPlaidSettingsOpen;

        [ObservableProperty]
        private PlaidEnvironmentSettingsViewModel? _plaidSettingsVM;

        [ObservableProperty]
        private bool _isEditorOpen;

        [ObservableProperty]
        private ProjectionItemEditorViewModel? _editorVM;

        [ObservableProperty]
        private bool _isBalanceCorrectionOpen;

        [ObservableProperty]
        private BalanceCorrectionViewModel? _balanceCorrectionVM;

        [ObservableProperty]
        private string _findText = string.Empty;

        [ObservableProperty]
        private SearchResult? _selectedSearchResult;

        [ObservableProperty]
        private Account? _selectedAccount;

        [ObservableProperty]
        private AccountListItemViewModel? _selectedAccountEntry;

        // Every account now gets the same projection/matching-capable view
        // — rebuilt via _accountVMFactory whenever SelectedAccount changes,
        // rather than one fixed DI singleton bound to a single account.
        [ObservableProperty]
        private AccountProjectionViewModel _accountVM = null!;

        public ShellViewModel(
            Func<Account, AccountProjectionViewModel> accountVMFactory,
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
            SecretStoreFactory secretStores,
            UnseenTransactionsTracker unseenTracker)
        {
            StatusVM = statusVM;
            _accountVMFactory = accountVMFactory;
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
            _unseenTracker = unseenTracker;
            SearchResults = new ObservableCollection<SearchResult>();

            // Guarantees at least one Account row exists — previously this
            // happened implicitly because AccountProjectionViewModel (a DI
            // singleton) called this in its own constructor before
            // ShellViewModel's ever ran; now that it's built on demand per
            // selected account instead, nothing else guarantees it.
            _accountRepo.GetOrCreateActiveAccount();

            Accounts = new ObservableCollection<AccountListItemViewModel>(
                accountRepo.GetAll().Select(a => new AccountListItemViewModel(a)));
            RefreshAccountListEntries();
            SelectedAccountEntry = Accounts.FirstOrDefault(e => e.Account.IsActive) ?? Accounts.FirstOrDefault();
            StatusVM.Report("Ready");

            // Plaid's /transactions/sync is the cheap, cursor-based
            // incremental endpoint — it's meant to be polled, so checking
            // every 10 minutes is nowhere near a rate-limiting concern.
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _syncTimer.Tick += (_, _) => _ = RunBackgroundSyncAsync();
            _syncTimer.Start();
        }

        public StatusBarViewModel StatusVM { get; }

        public ObservableCollection<SearchResult> SearchResults { get; }

        public ObservableCollection<AccountListItemViewModel> Accounts { get; }

        public bool HasSelectedAccount => SelectedAccount is not null;

        // The shared header's balance block — sourced directly from
        // SelectedAccount (which already carries StartingBalance/
        // StartingBalanceAsOf) rather than proxying through AccountVM,
        // since a sync only updates the latter in place.
        public string SelectedAccountBalanceLine =>
            SelectedAccount is null ? string.Empty : $"Balance: {SelectedAccount.StartingBalance:C2}";

        public string SelectedAccountBalanceAsOfLine =>
            SelectedAccount?.StartingBalanceAsOf is { } asOf
                ? $"Via Plaid, {asOf.ToLocalTime():yyyy-MM-dd HH:mm}"
                : "Not yet synced from Plaid";

        partial void OnSelectedAccountChanged(Account? value)
        {
            OnPropertyChanged(nameof(HasSelectedAccount));
            OnPropertyChanged(nameof(SelectedAccountBalanceLine));
            OnPropertyChanged(nameof(SelectedAccountBalanceAsOfLine));

            if (value is not null)
            {
                AccountVM = _accountVMFactory(value);

                // The user is now looking at this account, so whatever came
                // in since the last time they looked is no longer "unseen".
                _unseenTracker.MarkSeen(value.Id);
                var entry = Accounts.FirstOrDefault(e => e.Id == value.Id);
                if (entry is not null)
                    entry.HasUnseenTransactions = false;
            }
        }

        // EditSelected/PushForwardSelected act on AccountVM's own
        // selection, which lives on a different object — the generated
        // RelayCommands don't auto-recompute CanExecute for a property
        // change on someone else's VM, so poke them explicitly. Re-wired
        // every time AccountVM itself is replaced (switching accounts).
        partial void OnAccountVMChanged(AccountProjectionViewModel? oldValue, AccountProjectionViewModel newValue)
        {
            if (oldValue is not null)
                oldValue.PropertyChanged -= AccountVM_PropertyChanged;
            newValue.PropertyChanged += AccountVM_PropertyChanged;
        }

        private void AccountVM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccountProjectionViewModel.SelectedTransaction))
            {
                EditSelectedCommand.NotifyCanExecuteChanged();
                PushForwardSelectedCommand.NotifyCanExecuteChanged();
                CorrectBalanceSelectedCommand.NotifyCanExecuteChanged();
            }
        }

        // Keeps SelectedAccount (the plain Account used everywhere else in
        // the shell) in sync with whichever row the dropdown — now bound to
        // the richer AccountListItemViewModel rows — has selected.
        partial void OnSelectedAccountEntryChanged(AccountListItemViewModel? value)
        {
            SelectedAccount = value?.Account;
        }

        // SelectedAccount and AccountVM's internal account are two
        // independent in-memory copies of the same database row — a sync
        // only updates the latter directly, so the shared header needs its
        // own explicit refresh to avoid showing a stale balance after sync.
        public void RefreshSelectedAccountHeader()
        {
            if (SelectedAccount is null)
                return;

            var fresh = _accountRepo.GetAll().FirstOrDefault(a => a.Id == SelectedAccount.Id);
            if (fresh is null)
                return;

            SelectedAccount.StartingBalance = fresh.StartingBalance;
            SelectedAccount.StartingBalanceAsOf = fresh.StartingBalanceAsOf;
            OnPropertyChanged(nameof(SelectedAccountBalanceLine));
            OnPropertyChanged(nameof(SelectedAccountBalanceAsOfLine));

            RefreshAccountListEntries();
        }

        // Pushes fresh balances (and unseen-transaction flags) into the
        // dropdown's rows, and picks up any account PlaidSyncService's
        // account-discovery pass just added that isn't in the dropdown yet.
        // Called after every sync.
        public void RefreshAccountListEntries()
        {
            var fresh = _accountRepo.GetAll().ToDictionary(a => a.Id);

            foreach (var account in fresh.Values)
            {
                if (!Accounts.Any(e => e.Id == account.Id))
                    Accounts.Add(new AccountListItemViewModel(account));
            }

            foreach (var entry in Accounts)
            {
                if (fresh.TryGetValue(entry.Id, out var account))
                    entry.UpdateBalance(account.StartingBalance);

                // The account currently on screen just had its own data
                // refreshed above/alongside this call, so whatever came in
                // for it counts as seen rather than flagging a badge on the
                // very row the user is already looking at.
                if (entry.Id == SelectedAccount?.Id)
                    _unseenTracker.MarkSeen(entry.Id);

                entry.HasUnseenTransactions = _unseenTracker.GetUnseenCount(entry.Id) > 0;
            }
        }

        // Discovers any not-yet-local accounts under the Plaid Item,
        // re-syncs with Plaid, and refreshes every view that shows bank
        // data. Shared by app startup, the periodic timer, and a manual
        // "Reconnect Bank" so there's exactly one place that knows what
        // "a sync just happened" means for the UI.
        public async Task RunBackgroundSyncAsync()
        {
            if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
                return;

            try
            {
                await _syncService.DiscoverAccountsAsync();
                var changedCount = await _syncService.SyncAsync(_accountRepo.GetAll());
                if (changedCount > 0)
                    _matchingService.FindAndRecordPresumptiveMatches(AccountVM.AccountId);

                var tcs = new TaskCompletionSource();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // Always refreshed, even when nothing new came down —
                        // this updates the balance/"as of" figures in place on
                        // the existing view-model objects (every sync re-reads
                        // the account's current balance from Plaid), so it
                        // never visibly disturbs the grid. Also picks up any
                        // newly-discovered account into the dropdown.
                        AccountVM.RefreshAccountFromDatabase();
                        RefreshSelectedAccountHeader();

                        // ReloadTransactionsFromDatabase clears and rebuilds
                        // its collection from scratch — visible as a flash
                        // in the grid — so skip it on the (overwhelmingly
                        // common) periodic pass that found nothing new.
                        if (changedCount > 0)
                            AccountVM.ReloadTransactionsFromDatabase();
                    }
                    finally
                    {
                        tcs.SetResult();
                    }
                });
                await tcs.Task;
            }
            finally
            {
                Interlocked.Exchange(ref _isSyncing, 0);
            }
        }

        partial void OnFindTextChanged(string value)
        {
            UpdateSearchResults();
        }

        [RelayCommand]
        public void AddNew() => OpenEditor(target: null);

        [RelayCommand(CanExecute = nameof(CanActOnSelection))]
        public void EditSelected() => OpenEditor(AccountVM.SelectedTransaction);

        [RelayCommand(CanExecute = nameof(CanActOnSelection))]
        public void PushForwardSelected() => AccountVM.PushForwardCommand.Execute(AccountVM.SelectedTransaction);

        private bool CanActOnSelection() => AccountVM?.SelectedTransaction is { IsEditable: true };

        [RelayCommand(CanExecute = nameof(CanCorrectBalanceOnSelection))]
        public void CorrectBalanceSelected()
        {
            if (AccountVM.SelectedTransaction is not { } target)
                return;

            var vm = new BalanceCorrectionViewModel(AccountVM, target);
            vm.RequestClose += () =>
            {
                IsBalanceCorrectionOpen = false;
                BalanceCorrectionVM = null;
                AccountVM.FocusGridAfterPopoverClosed();
            };
            BalanceCorrectionVM = vm;
            IsBalanceCorrectionOpen = true;
        }

        // Only a genuine real bank transaction has a balance worth
        // correcting — not an editable projection item (its Balance is
        // always a forward projection, never a fact to begin with), and
        // not the synthetic pending-gap placeholder (it's not a real
        // transaction at all; its own value is kept current automatically
        // by AccountProjectionViewModel.SyncPendingGapPlaceholder).
        private bool CanCorrectBalanceOnSelection() =>
            AccountVM?.SelectedTransaction is { IsReadOnly: true, IsSyntheticPendingGap: false };

        private void OpenEditor(ProjectionItemViewModel? target)
        {
            var vm = new ProjectionItemEditorViewModel(AccountVM, target);
            vm.RequestClose += () =>
            {
                IsEditorOpen = false;
                EditorVM = null;
                AccountVM.FocusGridAfterPopoverClosed();
            };
            EditorVM = vm;
            IsEditorOpen = true;
        }

        [RelayCommand]
        public void Undo() => AccountVM.UndoCommand.Execute(null);

        [RelayCommand]
        public void Redo() => AccountVM.RedoCommand.Execute(null);

        [RelayCommand]
        public void OpenHistory()
        {
            var vm = new ChangeHistoryViewModel(_changeHistory, AccountVM.AccountId, AccountVM.ReloadTransactionsFromDatabase);
            vm.RequestClose += () =>
            {
                IsChangeHistoryOpen = false;
                ChangeHistoryVM = null;
                AccountVM.FocusGridAfterPopoverClosed();
            };
            ChangeHistoryVM = vm;
            IsChangeHistoryOpen = true;
        }

        [RelayCommand]
        public void OpenPlaidSettings()
        {
            var vm = new PlaidEnvironmentSettingsViewModel(_plaidSettings, _secretStores, _linkService);
            vm.RequestClose += () =>
            {
                IsPlaidSettingsOpen = false;
                PlaidSettingsVM = null;
                AccountVM.FocusGridAfterPopoverClosed();
            };
            PlaidSettingsVM = vm;
            IsPlaidSettingsOpen = true;
        }

        [RelayCommand]
        public void OpenMatches()
        {
            // Re-scan before showing results — matching otherwise only ever
            // ran once, right after the Plaid sync at startup, so items
            // added or edited mid-session were never considered. Also
            // expire any presumptive match whose projection item has since
            // moved outside the date window (e.g. pushed forward to its
            // next occurrence without ever being decided), so the review
            // list never shows a stale pairing.
            StatusVM.Report("Scanning for presumptive matches...", busy: true);
            var found = _matchingService.FindAndRecordPresumptiveMatches(AccountVM.AccountId);
            var expired = _matchingService.ExpireStaleMatches(AccountVM.AccountId);
            var foundMessage = found.Count > 0 ? $"Found {found.Count} new presumptive match(es)." : "No new presumptive matches found.";
            var expiredMessage = expired > 0 ? $" Expired {expired} stale match(es)." : "";
            StatusVM.Report(foundMessage + expiredMessage, busy: false);

            var vm = new MatchReviewViewModel(_matchRepo, _itemRepo, _bankRepo, _changeHistory, AccountVM.AccountId, AccountVM.ReloadTransactionsFromDatabase);
            vm.RequestClose += () =>
            {
                IsMatchReviewOpen = false;
                MatchReviewVM = null;
                AccountVM.FocusGridAfterPopoverClosed();
            };
            MatchReviewVM = vm;
            IsMatchReviewOpen = true;
        }

        // Update-mode reconnect: repairs the existing Plaid Item in place
        // (no new Item, no new access_token, no Trial-plan slot consumed).
        // Available any time — not just when a sync has flagged a problem —
        // so a stale connection can be fixed proactively too.
        [RelayCommand]
        public async Task ReconnectBank()
        {
            var linkVm = new PlaidLinkViewModel(_linkService, isUpdateMode: true);
            var window = new PlaidLinkWindow(linkVm);
            await window.ShowAndWaitAsync();

            if (!linkVm.Succeeded)
                return;

            StatusVM.Report("Bank connection restored — syncing...", busy: true);
            _ = RunBackgroundSyncAsync();
        }

        [RelayCommand]
        public void OpenFind()
        {
            IsFindOpen = true;
            FindText = string.Empty;
            SearchResults.Clear();
            SelectedSearchResult = null;
        }

        [RelayCommand]
        public void CancelFind()
        {
            IsFindOpen = false;
            AccountVM.FocusGridAfterPopoverClosed();
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

        public void OnFindKeyDown(KeyRoutedEventArgs e)
        {
            if (!IsFindOpen)
                return;

            switch (e.Key)
            {
                case VirtualKey.Enter:
                    e.Handled = true;
                    ConfirmFind();
                    break;
                case VirtualKey.Escape:
                    e.Handled = true;
                    CancelFind();
                    break;
                case VirtualKey.Down:
                    e.Handled = true;
                    MoveSelection(+1);
                    break;
                case VirtualKey.Up:
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
                .Transactions.Where(vm => !vm.IsSyntheticPendingGap)
                .Select(vm =>
                {
                    var lower = (vm.Name ?? string.Empty).ToLowerInvariant();
                    var hits = tokens.Count(tok => lower.Contains(tok));
                    return new { vm, hits };
                })
                .Where(x => x.hits > 0)
                .OrderByDescending(x => x.hits)
                .ThenByDescending(x => x.vm.TransactionDate)
                .ThenBy(x => x.vm.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new SearchResult(x.vm, x.hits));

            foreach (var r in results)
                SearchResults.Add(r);

            if (SearchResults.Count > 0)
                SelectedSearchResult = SearchResults[0];
        }

        public sealed class SearchResult
        {
            public SearchResult(ProjectionItemViewModel item, int hits)
            {
                Item = item;
                Hits = hits;
                Display = $"{item.TransactionDate:d}  •  {item.Name}";
            }

            public ProjectionItemViewModel Item { get; }
            public int Hits { get; }
            public string Display { get; }
        }
    }
}
