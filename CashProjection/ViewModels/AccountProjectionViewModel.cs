using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Models;
using CashProjection.Data.Repositories;
using CashProjection.Services;

namespace CashProjection.ViewModels
{
    public sealed partial class AccountProjectionViewModel : ObservableObject
    {
        private static readonly TransactionComparer s_comparer = new();

        private readonly StatusBarViewModel? _statusVM;
        private readonly AccountRepository _accountRepo;
        private readonly ProjectionItemRepository _itemRepo;
        private readonly BankTransactionRepository _bankRepo;
        private readonly MatchRepository _matchRepo;
        private readonly AppSettingsRepository _settingsRepo;
        private readonly ChangeHistoryService _changeHistory;
        private readonly Account _account;

        // Tracks each item's last-persisted state so Save() can diff and
        // only write+record history for fields that actually changed,
        // rather than recording a change-history entry on every keystroke.
        private readonly Dictionary<ProjectionItemViewModel, string> _lastPersistedSnapshots = new();
        private readonly Stack<int> _redoStack = new();

        [ObservableProperty]
        private string _accountName = "My Account";

        [ObservableProperty]
        private string _currentBalanceDisplay = string.Empty;

        [ObservableProperty]
        private bool _isDirty;

        // Re-entrancy guard for ResortAndRecalculate
        private bool _isRecalculating;

        [ObservableProperty]
        private bool _isSearchOpen;

        [ObservableProperty]
        private string _searchText = string.Empty;

        private ObservableCollection<ProjectionItemViewModel> _transactions;
        private WeakReference<FrameworkElement>? _viewRef;

        public AccountProjectionViewModel(
            AccountRepository accountRepo,
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            MatchRepository matchRepo,
            AppSettingsRepository settingsRepo,
            ChangeHistoryService changeHistory,
            StatusBarViewModel? statusVM = null)
        {
            _statusVM = statusVM;
            _accountRepo = accountRepo;
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _matchRepo = matchRepo;
            _settingsRepo = settingsRepo;
            _changeHistory = changeHistory;

            _account = _accountRepo.GetOrCreateActiveAccount();
            _accountName = _account.Name;
            _currentBalanceDisplay = FormatBalanceDisplay();

            _transactions = [];
            _transactions.CollectionChanged += Transactions_CollectionChanged;

            LoadAllRows();

            foreach (var t in _transactions)
                t.PropertyChanged += Transaction_PropertyChanged;

            ApplySort();
            ResortAndRecalculate();
            RebuildSnapshots();

            _isDirty = false;
        }

        // Populates _transactions with both user-editable projection items
        // and read-only rows mirroring every real BankTransaction — the grid
        // always shows real transactions, not only once matched.
        private void LoadAllRows()
        {
            _transactions.Clear();

            var items = _itemRepo.GetByAccount(_account.Id).Select(i => new ProjectionItemViewModel(i)).ToList();
            var bankRows = _bankRepo.GetByAccount(_account.Id).Select(ProjectionItemViewModel.FromBankTransaction).ToList();

            ApplyConfirmedMatches(items, bankRows);

            _transactions.AddRange(items);
            _transactions.AddRange(bankRows);
        }

        // A confirmed match "crosses off" the real BankTransaction
        // permanently — that row stays flagged as matched forever,
        // regardless of what happens afterward, because it's a fact about
        // that specific real transaction. The projection item side is
        // different: Accept() reschedules it to its next occurrence, so the
        // *live* item no longer represents the occurrence that was actually
        // matched. We only pair the live item into the match's sort/visual
        // grouping while its date still equals the occurrence that was
        // matched (MatchedAtItemDate); once rescheduled past that, the item
        // reverts to sorting/displaying as a normal, unmatched row, while
        // the bank transaction keeps its link icon on its own.
        private void ApplyConfirmedMatches(List<ProjectionItemViewModel> items, List<ProjectionItemViewModel> bankRows)
        {
            var driftThreshold = decimal.TryParse(_settingsRepo.Get("AmountDriftWarningThreshold"), out var t) ? t : 10m;

            foreach (var match in _matchRepo.GetByStatus(Models.MatchStatus.Confirmed))
            {
                var item = items.FirstOrDefault(i => i.Id == match.ProjectionItemId);
                var bankRow = bankRows.FirstOrDefault(b => b.BankTransactionId == match.BankTransactionId);
                if (bankRow is null)
                    continue;

                var groupId = Guid.NewGuid();
                bankRow.MatchGroupId = groupId;
                bankRow.MatchScore = match.Score;

                var stillSameOccurrence = item is not null
                    && match.MatchedAtItemDate.HasValue
                    && item.TransactionDate.Date == match.MatchedAtItemDate.Value.Date;

                if (stillSameOccurrence)
                {
                    item!.MatchGroupId = groupId;
                    item.MatchScore = match.Score;
                    item.EffectiveSortDate = bankRow.TransactionDate;

                    var projectedAmount = item.Payment ?? item.Deposit ?? 0m;
                    var realAmount = bankRow.Payment ?? bankRow.Deposit ?? 0m;
                    var drift = Math.Abs(projectedAmount - realAmount) > driftThreshold;
                    item.HasAmountDrift = drift;
                    bankRow.HasAmountDrift = drift;
                }
            }
        }

        public int AccountId => _account.Id;

        public ObservableCollection<ProjectionItemViewModel> Transactions
        {
            get => _transactions;
            set => SetProperty(ref _transactions, value);
        }

        [RelayCommand]
        public void AddNew()
        {
            var model = new ProjectionItem
            {
                AccountId = _account.Id,
                Name = string.Empty,
                TransactionDate = DateTime.Today,
                Deposit = null,
                Payment = null,
                Periodicity = Periodicity.Monthly
            };
            _itemRepo.Insert(model);
            _changeHistory.Record(ChangeEntityType.ProjectionItem, model.Id, ChangeType.Added, before: null, after: model, "Added a new projection item");
            _redoStack.Clear();

            var newTransaction = new ProjectionItemViewModel(model);
            _transactions.Add(newTransaction);
            _lastPersistedSnapshots[newTransaction] = JsonSerializer.Serialize(model);
            IsDirty = true;

            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (_viewRef == null || !_viewRef.TryGetTarget(out var fe))
                        return;

                    if (fe.FindName("TransactionsGrid") is not DataGrid dg)
                        return;

                    dg.SelectedItem = newTransaction;
                    var dateColumn = GetColumnByHeader(dg, "Date") ?? dg.Columns.FirstOrDefault();
                    if (dateColumn is null)
                    {
                        dg.Focus();
                        return;
                    }

                    dg.CurrentCell = new DataGridCellInfo(newTransaction, dateColumn);
                    dg.ScrollIntoView(newTransaction, dateColumn);
                    dg.UpdateLayout();
                    dg.BeginEdit();
                })
            );
        }

        public void CommitPendingEdits()
        {
            var view = CollectionViewSource.GetDefaultView(Transactions);
            if (view is IEditableCollectionView ecv)
            {
                if (ecv.IsEditingItem)
                    ecv.CommitEdit();
                if (ecv.IsAddingNew)
                    ecv.CommitNew();
            }
        }

        [RelayCommand(CanExecute = nameof(CanDeleteOrPushForward))]
        public void DeleteTransaction(ProjectionItemViewModel? transaction)
        {
            if (transaction == null)
                return;

            string amountText = transaction.Deposit.HasValue
                ? $"deposit of {transaction.Deposit:C}"
                : transaction.Payment.HasValue
                    ? $"payment of {transaction.Payment:C}"
                    : "amount of $0";

            var result = MessageBox.Show(
                $"Are you sure you want to delete '{transaction.Name}' {amountText} on {transaction.TransactionDate:d}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                var before = transaction.ToModel();
                _itemRepo.Delete(transaction.Id);
                _changeHistory.Record(ChangeEntityType.ProjectionItem, transaction.Id, ChangeType.Deleted, before, after: null, $"Deleted '{transaction.Name}'");
                _redoStack.Clear();
                _lastPersistedSnapshots.Remove(transaction);

                _transactions.Remove(transaction);
                IsDirty = true;
            }
        }

        public void FocusTransaction(ProjectionItemViewModel item)
        {
            if (item is null)
                return;

            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (_viewRef == null || !_viewRef.TryGetTarget(out var fe))
                        return;

                    if (fe.FindName("TransactionsGrid") is not DataGrid dg)
                        return;

                    dg.CommitEdit(DataGridEditingUnit.Cell, true);
                    dg.CommitEdit(DataGridEditingUnit.Row, true);

                    dg.SelectedItem = item;
                    var targetColumn = ChooseTargetColumn(dg, item) ?? dg.Columns.FirstOrDefault();
                    if (targetColumn is null)
                    {
                        dg.Focus();
                        return;
                    }

                    dg.CurrentCell = new DataGridCellInfo(item, targetColumn);
                    dg.ScrollIntoView(item, targetColumn);
                    dg.UpdateLayout();
                    dg.BeginEdit();

                    var cellContent = targetColumn.GetCellContent(item);
                    if (cellContent != null)
                    {
                        if (FindVisualChild<TextBox>(cellContent) is TextBox editorTb)
                        {
                            editorTb.Focus();
                            editorTb.SelectAll();
                            Keyboard.Focus(editorTb);
                        }
                        else
                        {
                            if (cellContent is Control ctrl)
                            {
                                ctrl.Focus();
                                Keyboard.Focus(ctrl);
                            }
                        }
                    }
                    else
                    {
                        if (dg.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                        {
                            row.IsSelected = true;
                            row.BringIntoView();
                            row.Focus();
                        }
                    }
                })
            );
        }

        [RelayCommand(CanExecute = nameof(CanDeleteOrPushForward))]
        public void PushForward(ProjectionItemViewModel? transaction)
        {
            if (transaction == null)
                return;

            var before = transaction.ToModel();
            ProjectionRescheduler.Advance(transaction, Transactions, realizedAmount: null);
            var after = transaction.ToModel();

            _itemRepo.Update(after);
            _changeHistory.Record(ChangeEntityType.ProjectionItem, transaction.Id, ChangeType.Rescheduled, before, after, $"Pushed '{transaction.Name}' forward — {ChangeDescriber.DescribeProjectionItemDiff(before, after)}");
            _redoStack.Clear();
            _lastPersistedSnapshots[transaction] = JsonSerializer.Serialize(after);

            IsDirty = true;
        }

        [RelayCommand]
        public void Save()
        {
            _account.Name = AccountName;
            _accountRepo.Update(_account);

            var changedCount = 0;
            foreach (var vm in _transactions.Where(t => !t.IsReadOnly))
            {
                var current = vm.ToModel();
                var currentJson = JsonSerializer.Serialize(current);

                if (_lastPersistedSnapshots.TryGetValue(vm, out var previousJson) && previousJson != currentJson)
                {
                    var previous = JsonSerializer.Deserialize<ProjectionItem>(previousJson)!;
                    _itemRepo.Update(current);
                    _changeHistory.Record(ChangeEntityType.ProjectionItem, current.Id, ChangeType.Modified, previous, current, $"Edited '{current.Name}' — {ChangeDescriber.DescribeProjectionItemDiff(previous, current)}");
                    _lastPersistedSnapshots[vm] = currentJson;
                    changedCount++;
                }
            }
            if (changedCount > 0)
                _redoStack.Clear();

            IsDirty = false;
            _statusVM?.Report(
                $"Saved {changedCount} change(s) to {Data.CashProjectionDbContext.GetDefaultDbPath()}",
                busy: false,
                StatusSeverity.Info);
        }

        [RelayCommand]
        public void Undo()
        {
            var latest = _changeHistory.GetAllNewestFirst().FirstOrDefault();
            if (latest is null)
                return;

            _changeHistory.Undo(latest.Id);
            _redoStack.Push(latest.Id);
            ReloadTransactionsFromDatabase();
            _statusVM?.Report($"Undid: {latest.Description}", busy: false);
        }

        [RelayCommand]
        public void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            var entryId = _redoStack.Pop();
            _changeHistory.Redo(entryId);
            ReloadTransactionsFromDatabase();
            _statusVM?.Report("Redo applied", busy: false);
        }

        // Discards in-memory state and reloads everything from the database
        // — used after Undo/Redo/RollbackTo, since those mutate rows directly
        // rather than going through this view model's own collection.
        public void ReloadTransactionsFromDatabase()
        {
            foreach (var t in _transactions)
                t.PropertyChanged -= Transaction_PropertyChanged;
            _transactions.CollectionChanged -= Transactions_CollectionChanged;

            LoadAllRows();

            foreach (var t in _transactions)
                t.PropertyChanged += Transaction_PropertyChanged;
            _transactions.CollectionChanged += Transactions_CollectionChanged;

            ApplySort();
            ResortAndRecalculate();
            RebuildSnapshots();
            IsDirty = false;
        }

        private void RebuildSnapshots()
        {
            _lastPersistedSnapshots.Clear();
            foreach (var t in _transactions.Where(t => !t.IsReadOnly))
                _lastPersistedSnapshots[t] = JsonSerializer.Serialize(t.ToModel());
        }

        // Re-reads the account row from the database and refreshes display
        // state. Used after a background Plaid sync updates the real
        // starting balance, so the UI doesn't go stale and a subsequent
        // Save() doesn't overwrite it with an in-memory value.
        public void RefreshAccountFromDatabase()
        {
            var fresh = _accountRepo.GetOrCreateActiveAccount();
            _account.Name = fresh.Name;
            _account.StartingBalance = fresh.StartingBalance;
            _account.StartingBalanceAsOf = fresh.StartingBalanceAsOf;
            _account.PlaidItemId = fresh.PlaidItemId;
            _account.PlaidAccountId = fresh.PlaidAccountId;

            AccountName = _account.Name;
            CurrentBalanceDisplay = FormatBalanceDisplay();
            ResortAndRecalculate();
        }

        private string FormatBalanceDisplay() =>
            _account.StartingBalanceAsOf.HasValue
                ? $"Current balance: {_account.StartingBalance:C2} (as of {_account.StartingBalanceAsOf.Value.ToLocalTime():g}, from Plaid)"
                : $"Current balance: {_account.StartingBalance:C2} (not yet synced from Plaid)";

        public void SetViewReference(FrameworkElement view)
        {
            _viewRef = new WeakReference<FrameworkElement>(view);
        }

        [RelayCommand]
        public void ToggleSearch()
        {
            IsSearchOpen = !IsSearchOpen;
        }

        private static DataGridColumn? ChooseTargetColumn(DataGrid dg, ProjectionItemViewModel item)
        {
            if (item.Deposit.HasValue)
                return GetColumnByHeader(dg, "Deposit");
            if (item.Payment.HasValue)
                return GetColumnByHeader(dg, "Payment");

            return GetColumnByHeader(dg, "Name") ?? dg.Columns.FirstOrDefault();
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    return t;
                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static DataGridColumn? GetColumnByHeader(DataGrid dg, string header) =>
            dg.Columns.FirstOrDefault(c =>
                c.Header is string s && s.Equals(header, StringComparison.CurrentCultureIgnoreCase)
            );

        private void ApplySort()
        {
            var view = CollectionViewSource.GetDefaultView(Transactions);

            if (view is IEditableCollectionView editable && (editable.IsEditingItem || editable.IsAddingNew))
            {
                Application.Current?.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new System.Action(ApplySort)
                );
                return;
            }

            view.SortDescriptions.Clear();

            if (view is ListCollectionView listView)
            {
                listView.CustomSort = new TransactionComparer();
            }
            else
            {
                view.SortDescriptions.Add(
                    new SortDescription(
                        nameof(ProjectionItemViewModel.TransactionDate),
                        ListSortDirection.Descending
                    )
                );
            }

            if (view is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
            {
                live.IsLiveSorting = true;
                live.LiveSortingProperties?.Clear();
                live.LiveSortingProperties?.Add(nameof(ProjectionItemViewModel.TransactionDate));
                live.LiveSortingProperties?.Add(nameof(ProjectionItemViewModel.EffectiveSortDate));
                live.LiveSortingProperties?.Add(nameof(ProjectionItemViewModel.MatchGroupId));
                live.LiveSortingProperties?.Add(nameof(ProjectionItemViewModel.Payment));
                live.LiveSortingProperties?.Add(nameof(ProjectionItemViewModel.Deposit));
            }
        }

        private void FocusSearch()
        {
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (_viewRef != null && _viewRef.TryGetTarget(out var fe))
                    {
                        if (fe.FindName("SearchTextBox") is TextBox tb)
                        {
                            tb.Focus();
                            tb.SelectAll();
                        }
                    }
                })
            );
        }

        partial void OnAccountNameChanged(string value)
        {
            IsDirty = true;
        }

        partial void OnIsSearchOpenChanged(bool value)
        {
            if (value)
                FocusSearch();
        }

        // Real bank transactions and projected items get their Balance
        // computed in two separate passes:
        //   - Real rows WITH a known balance (Plaid's own running_balance,
        //     reported by the bank) just show that value, untouched — it's
        //     a fact, not an estimate.
        //   - Real rows WITHOUT one (not every institution/transaction gets
        //     running_balance) fall back to an estimate: walked backward
        //     from the current real balance (Account.StartingBalance)
        //     through real transactions only, grouped by calendar date since
        //     Plaid gives no reliable time-of-day to order same-day rows
        //     honestly. Never influenced by projected items.
        //   - Projected rows: a forward-looking estimate, starting from the
        //     same current balance and advanced only through other
        //     projected items in date order.
        private void ResortAndRecalculate()
        {
            if (_isRecalculating)
                return;

            _isRecalculating = true;
            try
            {
                var realByDateDescending = _transactions
                    .Where(t => t.IsReadOnly && !t.HasKnownBalance)
                    .GroupBy(t => t.TransactionDate.Date)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                var runningReal = _account.StartingBalance;
                foreach (var dateGroup in realByDateDescending)
                {
                    foreach (var t in dateGroup)
                        t.Balance = runningReal; // balance as of end of this date

                    // Step back to the balance as it stood before this date's
                    // transactions, i.e. after the previous date's.
                    var netEffect = dateGroup.Sum(t => (t.Deposit ?? 0m) - (t.Payment ?? 0m));
                    runningReal -= netEffect;
                }

                // Plain chronological order for the balance math — independent
                // of the display comparer's match-group adjacency/descending rules.
                var projectedAscending = _transactions
                    .Where(t => !t.IsReadOnly)
                    .OrderBy(t => t.TransactionDate)
                    .ToList();

                var runningProjected = _account.StartingBalance;
                foreach (var t in projectedAscending)
                {
                    if (t.Deposit.HasValue && t.Deposit.Value != 0)
                        runningProjected += t.Deposit.Value;
                    else if (t.Payment.HasValue && t.Payment.Value != 0)
                        runningProjected -= t.Payment.Value;

                    t.Balance = runningProjected;
                }

                var ordered = _transactions.OrderBy(t => t, s_comparer).ToList();

                var now = DateTime.Now.Date;
                var start = now.AddMonths(-1);
                var end = now.AddMonths(1);

                var candidates = ordered
                    .Where(t => t.TransactionDate.Date >= start && t.TransactionDate.Date <= end)
                    .ToList();
                ProjectionItemViewModel? lowest = null;
                if (candidates.Count > 0)
                {
                    var minBal = candidates.Min(t => t.Balance);
                    lowest = candidates.FirstOrDefault(t => t.Balance == minBal);
                }

                foreach (var t in ordered)
                    t.IsLowestNearNow = ReferenceEquals(t, lowest);

                // Projection items more than a configurable number of days
                // past due, with no presumptive/confirmed match yet, need a
                // visual flag — could mean the bill hasn't hit the account
                // yet, was forgotten, or the schedule needs adjusting.
                var overdueThresholdDays = int.TryParse(_settingsRepo.Get("OverdueWarningDays"), out var d) ? d : 2;
                var overdueCutoff = now.AddDays(-overdueThresholdDays);
                foreach (var t in _transactions.Where(t => !t.IsReadOnly))
                    t.IsOverdueUnmatched = !t.MatchGroupId.HasValue && t.TransactionDate.Date < overdueCutoff;
            }
            finally
            {
                _isRecalculating = false;
            }
        }

        private void Transaction_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "Balance")
                return;

            if (e.PropertyName
                is nameof(ProjectionItemViewModel.Name)
                    or nameof(ProjectionItemViewModel.TransactionDate)
                    or nameof(ProjectionItemViewModel.Deposit)
                    or nameof(ProjectionItemViewModel.Payment)
                    or nameof(ProjectionItemViewModel.Periodicity))
            {
                IsDirty = true;
                ResortAndRecalculate();
            }
        }

        private void Transactions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (ProjectionItemViewModel t in e.NewItems)
                    t.PropertyChanged += Transaction_PropertyChanged;

            if (e.OldItems != null)
                foreach (ProjectionItemViewModel t in e.OldItems)
                    t.PropertyChanged -= Transaction_PropertyChanged;

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var t in _transactions)
                {
                    t.PropertyChanged -= Transaction_PropertyChanged;
                    t.PropertyChanged += Transaction_PropertyChanged;
                }
            }

            IsDirty = true;
            ResortAndRecalculate();
        }

        private sealed class TransactionComparer : System.Collections.IComparer, IComparer<ProjectionItemViewModel>
        {
            public int Compare(object? x, object? y)
            {
                if (x is ProjectionItemViewModel a && y is ProjectionItemViewModel b)
                    return Compare(a, b);

                if (ReferenceEquals(x, y))
                    return 0;
                if (x is not ProjectionItemViewModel)
                    return -1;
                return 1;
            }

            public int Compare(ProjectionItemViewModel? a, ProjectionItemViewModel? b)
            {
                if (ReferenceEquals(a, b))
                    return 0;
                if (a is null)
                    return -1;
                if (b is null)
                    return 1;

                // Date descending — most recent/soonest first. A matched
                // projection item borrows its real transaction's date here
                // (EffectiveSortDate) so the pair sorts together.
                int c = b.EffectiveSortDate.CompareTo(a.EffectiveSortDate);
                if (c != 0)
                    return c;

                // Matched pairs cluster by their shared group, with the real
                // (fact) row immediately before its projected counterpart.
                if (a.MatchGroupId != b.MatchGroupId)
                {
                    var aGroup = a.MatchGroupId?.ToString() ?? string.Empty;
                    var bGroup = b.MatchGroupId?.ToString() ?? string.Empty;
                    return string.Compare(aGroup, bGroup, StringComparison.Ordinal);
                }
                if (a.MatchGroupId.HasValue && a.IsReadOnly != b.IsReadOnly)
                    return a.IsReadOnly ? -1 : 1;

                bool aIsPayment = a.Payment.HasValue;
                bool bIsPayment = b.Payment.HasValue;
                if (aIsPayment != bIsPayment)
                    return aIsPayment ? -1 : 1;

                c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0)
                    return c;

                var aAmt = a.Payment ?? a.Deposit ?? 0m;
                var bAmt = b.Payment ?? b.Deposit ?? 0m;
                return aAmt.CompareTo(bAmt);
            }
        }

        private bool CanDeleteOrPushForward(ProjectionItemViewModel? transaction)
        {
            // Return false during binding resolution to prevent MS.Internal.NamedObject issues.
            // Real bank-transaction rows are read-only — nothing to push forward or delete.
            return transaction is not null && !transaction.IsReadOnly;
        }
    }
}
