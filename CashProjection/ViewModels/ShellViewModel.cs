using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using CashProjection.Data.Repositories;
using CashProjection.Models;
using CashProjection.Services;
using CashProjection.Services.Dialogs;
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
        private readonly IDialogService _dialogService;
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
            SecretStoreFactory secretStores,
            UnseenTransactionsTracker unseenTracker,
            IDialogService dialogService)
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
            _unseenTracker = unseenTracker;
            _dialogService = dialogService;
            SearchResults = new ObservableCollection<SearchResult>();
            Accounts = new ObservableCollection<AccountListItemViewModel>(
                accountRepo.GetAll().Select(a => new AccountListItemViewModel(a)));
            RefreshAccountListEntries();
            SelectedAccountEntry = Accounts.FirstOrDefault(e => e.Account.SupportsProjections) ?? Accounts.FirstOrDefault();
            StatusVM.Report("Ready");

            // Plaid's /transactions/sync is the cheap, cursor-based
            // incremental endpoint — it's meant to be polled, so checking
            // every 10 minutes is nowhere near a rate-limiting concern.
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _syncTimer.Tick += (_, _) => _ = RunBackgroundSyncAsync();
            _syncTimer.Start();

            // EditSelected/PushForwardSelected act on AccountVM's own
            // selection, which lives on a different object — the generated
            // RelayCommands don't auto-recompute CanExecute for a property
            // change on someone else's VM, so poke them explicitly.
            AccountVM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AccountProjectionViewModel.SelectedTransaction))
                {
                    EditSelectedCommand.NotifyCanExecuteChanged();
                    PushForwardSelectedCommand.NotifyCanExecuteChanged();
                    CorrectBalanceSelectedCommand.NotifyCanExecuteChanged();
                }
            };
        }

        public AccountProjectionViewModel AccountVM { get; }

        public StatusBarViewModel StatusVM { get; }

        public ObservableCollection<SearchResult> SearchResults { get; }

        public ObservableCollection<AccountListItemViewModel> Accounts { get; }

        // Checking is the one account with projections/matching — the
        // toolbar's editing actions and the grid only make sense there.
        // Every other linked account (money market, savings, a CD, a line
        // of credit, ...) is balance-and-history-only.
        public bool IsCheckingSelected => SelectedAccount?.SupportsProjections == true;

        public bool IsOtherAccountSelected => SelectedAccount is not null && !IsCheckingSelected;

        // Window-rooted XAML can't use a Converter inside {x:Bind} (the
        // generated binding code needs a FrameworkElement "converter
        // lookup root," and Window — unlike WPF's — isn't one), so the
        // already-converted values are exposed directly instead.
        public Visibility CheckingVisibility => IsCheckingSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility OtherAccountVisibility => IsOtherAccountSelected ? Visibility.Visible : Visibility.Collapsed;

        // The shared header's balance block — sourced directly from
        // SelectedAccount (which already carries StartingBalance/
        // StartingBalanceAsOf) rather than proxying through AccountVM or
        // OtherAccountVM, since the header has to work identically no
        // matter which kind of account is currently selected.
        public string SelectedAccountBalanceLine =>
            SelectedAccount is null ? string.Empty : $"Balance: {SelectedAccount.StartingBalance:C2}";

        public string SelectedAccountBalanceAsOfLine =>
            SelectedAccount?.StartingBalanceAsOf is { } asOf
                ? $"Via Plaid, {asOf.ToLocalTime():yyyy-MM-dd HH:mm}"
                : "Not yet synced from Plaid";

        partial void OnSelectedAccountChanged(Account? value)
        {
            OnPropertyChanged(nameof(IsCheckingSelected));
            OnPropertyChanged(nameof(IsOtherAccountSelected));
            OnPropertyChanged(nameof(CheckingVisibility));
            OnPropertyChanged(nameof(OtherAccountVisibility));
            OnPropertyChanged(nameof(SelectedAccountBalanceLine));
            OnPropertyChanged(nameof(SelectedAccountBalanceAsOfLine));

            OtherAccountVM = value is not null && !value.SupportsProjections
                ? new AccountTransactionsViewModel(value, _bankRepo)
                : null;

            // The user is now looking at this account, so whatever came in
            // since the last time they looked is no longer "unseen".
            if (value is not null)
            {
                _unseenTracker.MarkSeen(value.Id);
                var entry = Accounts.FirstOrDefault(e => e.Id == value.Id);
                if (entry is not null)
                    entry.HasUnseenTransactions = false;
            }
        }

        // Keeps SelectedAccount (the plain Account used everywhere else in
        // the shell) in sync with whichever row the dropdown — now bound to
        // the richer AccountListItemViewModel rows — has selected.
        partial void OnSelectedAccountEntryChanged(AccountListItemViewModel? value)
        {
            SelectedAccount = value?.Account;
        }

        // Called after a sync completes, so whichever non-checking account
        // is currently on screen picks up fresh balance/transaction data
        // without the user having to re-select it.
        public void RefreshOtherAccountView() => OtherAccountVM?.Refresh();

        // SelectedAccount, AccountVM's internal account, and
        // OtherAccountVM's internal account are three independent
        // in-memory copies of the same database row — a sync only updates
        // the latter two directly, so the shared header needs its own
        // explicit refresh to avoid showing a stale balance after sync.
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
        // dropdown's rows. Called after every sync — the dropdown is a
        // separate set of view-model objects from SelectedAccount/AccountVM/
        // OtherAccountVM, so it needs its own explicit refresh too.
        public void RefreshAccountListEntries()
        {
            var fresh = _accountRepo.GetAll().ToDictionary(a => a.Id);

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

        // Re-syncs with Plaid and refreshes every view that shows bank
        // data. Shared by app startup, the periodic timer, and a manual
        // "Reconnect Bank" so there's exactly one place that knows what
        // "a sync just happened" means for the UI.
        public async Task RunBackgroundSyncAsync()
        {
            if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
                return;

            try
            {
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
                        // never visibly disturbs the grid.
                        AccountVM.RefreshAccountFromDatabase();
                        RefreshSelectedAccountHeader();

                        // ReloadTransactionsFromDatabase/RefreshOtherAccountView
                        // clear and rebuild their collections from scratch —
                        // visible as a flash in the grid — so skip them on the
                        // (overwhelmingly common) periodic pass that found
                        // nothing new to show.
                        if (changedCount > 0)
                        {
                            AccountVM.ReloadTransactionsFromDatabase();
                            RefreshOtherAccountView();
                        }
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

        // Closes whichever single-instance overlay is currently open — the
        // one deterministic Escape-dismiss path shared by every popover
        // except Find (which has its own key handling for Up/Down/Enter
        // too). See ShellView.xaml.cs's Overlay_PreviewKeyDown for why this
        // doesn't just rely on each popover's own IsCancel button.
        public void CancelActiveOverlay()
        {
            if (IsEditorOpen)
                EditorVM?.CancelCommand.Execute(null);
            else if (IsBalanceCorrectionOpen)
                BalanceCorrectionVM?.CancelCommand.Execute(null);
            else if (IsMatchReviewOpen)
                MatchReviewVM?.CloseCommand.Execute(null);
            else if (IsChangeHistoryOpen)
                ChangeHistoryVM?.CloseCommand.Execute(null);
            else if (IsPlaidSettingsOpen)
                PlaidSettingsVM?.CloseCommand.Execute(null);
        }

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
            var vm = new ChangeHistoryViewModel(_changeHistory, _dialogService, AccountVM.ReloadTransactionsFromDatabase);
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
            var vm = new PlaidEnvironmentSettingsViewModel(_plaidSettings, _secretStores, _linkService, _dialogService);
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

            var vm = new MatchReviewViewModel(_matchRepo, _itemRepo, _bankRepo, _changeHistory, AccountVM.ReloadTransactionsFromDatabase);
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
