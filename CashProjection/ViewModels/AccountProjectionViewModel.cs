using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using CashProjection.Models;
using CashProjection.Data.Repositories;
using CashProjection.Services;
using CashProjection.Services.Dialogs;

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
        private readonly IDialogService _dialogService;
        private readonly Account _account;
        private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        private readonly Stack<int> _redoStack = new();

        // Re-entrancy guard for ResortAndRecalculate
        private bool _isRecalculating;

        [ObservableProperty]
        private bool _isSearchOpen;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ProjectionItemViewModel? _selectedTransaction;

        private ObservableCollection<ProjectionItemViewModel> _transactions;
        private WeakReference<FrameworkElement>? _viewRef;

        public AccountProjectionViewModel(
            Account account,
            AccountRepository accountRepo,
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            MatchRepository matchRepo,
            AppSettingsRepository settingsRepo,
            ChangeHistoryService changeHistory,
            IDialogService dialogService,
            StatusBarViewModel? statusVM = null)
        {
            _statusVM = statusVM;
            _accountRepo = accountRepo;
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _matchRepo = matchRepo;
            _settingsRepo = settingsRepo;
            _changeHistory = changeHistory;
            _dialogService = dialogService;

            _account = account;

            _transactions = [];

            // Deliberately NOT subscribed yet — LoadAllRows() populates via
            // several Clear()/Add() calls, each of which would otherwise
            // synchronously re-enter Transactions_CollectionChanged ->
            // ResortAndRecalculate() while still inside ObservableCollection's
            // own CollectionChanged dispatch for the PRIOR mutation, which
            // throws ("Cannot change ObservableCollection during a
            // CollectionChanged event") the moment ResortAndRecalculate
            // itself needs to add/remove the pending-gap placeholder row.
            // Subscribing only after the initial bulk population has fully
            // returned avoids that entirely — the same pattern
            // ReloadTransactionsFromDatabase already uses.
            LoadAllRows();

            foreach (var t in _transactions)
                t.PropertyChanged += Transaction_PropertyChanged;
            _transactions.CollectionChanged += Transactions_CollectionChanged;

            ResortAndRecalculate();
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

        // Inserts, updates in place, or removes the single synthetic
        // "pending/unexplained bank activity" placeholder row to match
        // gapAmount — the discrepancy ResortAndRecalculate's checkpoint-
        // aware backward walk just computed between today's live balance
        // and the most recent trusted checkpoint (Plaid-reported or
        // manually corrected) it found, if any. Called from inside that
        // same walk, every pass, so this reacts live to anything that
        // could introduce or resolve such a gap — ordinary Plaid sync
        // latency, or a manual correction that no longer reconciles with
        // the live balance. Mutating the existing instance's
        // [ObservableProperty]s in place (rather than replacing it) feeds
        // the existing PropertyChanged -> Transaction_PropertyChanged ->
        // ResortAndRecalculate() pipeline the same as any other edit —
        // safely a no-op re-entrant call, since _isRecalculating is still
        // true for the remainder of the pass that's already in progress.
        private void SyncPendingGapPlaceholder(decimal gapAmount)
        {
            var existing = _transactions.FirstOrDefault(t => t.IsSyntheticPendingGap);

            if (Math.Abs(gapAmount) < 0.01m)
            {
                if (existing is not null)
                    _transactions.Remove(existing);
                return;
            }

            if (existing is null)
            {
                var placeholder = ProjectionItemViewModel.CreatePendingGapPlaceholder(_account, gapAmount);
                if (placeholder is not null)
                    _transactions.Add(placeholder);
                return;
            }

            existing.TransactionDate = DateTime.Now;
            existing.EffectiveSortDate = DateTime.Now;
            // Balance is the one field ResortAndRecalculate's backward-
            // estimate walk will never set for this row (HasKnownBalance=
            // true exempts it) — this assignment is the only thing keeping
            // it current.
            existing.Balance = _account.StartingBalance;
            if (gapAmount > 0)
                existing.Deposit = gapAmount;
            else
                existing.Payment = -gapAmount;
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

                var stillSameOccurrence = item is not null && match.IsForCurrentOccurrenceOf(item.TransactionDate);

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

        // Builds a fresh scratch item (not yet inserted anywhere) for the
        // "new item" mode of the projection item editor popover — only
        // actually persisted if the user clicks Save there.
        public ProjectionItemViewModel BuildNewItemScratch() =>
            new(new ProjectionItem
            {
                AccountId = _account.Id,
                Name = string.Empty,
                TransactionDate = DateTime.Today,
                Deposit = null,
                Payment = null,
                Periodicity = Periodicity.Monthly
            });

        // Commits a new item created via the editor popover: inserts it,
        // records history, and adds it to the live grid.
        public void CommitNewItem(ProjectionItemViewModel scratch)
        {
            var model = scratch.ToModel();
            model.Id = 0;
            model.AccountId = _account.Id;
            model.UpdatedAtUtc = DateTime.UtcNow;

            _itemRepo.Insert(model);
            _changeHistory.Record(_account.Id, ChangeEntityType.ProjectionItem, model.Id, ChangeType.Added, before: null, after: model, $"Added '{model.Name}'");
            _redoStack.Clear();

            var newTransaction = new ProjectionItemViewModel(model);
            _transactions.Add(newTransaction);
            _statusVM?.Report($"Saved '{model.Name}'.", busy: false, StatusSeverity.Info);

            FocusTransaction(newTransaction);
        }

        // Applies edits made to the editor popover's scratch copy back onto
        // the live item and persists immediately — the editor popover is
        // the only place projection items get added or edited, so there's
        // no reason to defer this to a separate Save step: what you click
        // Save on is what's written to the DB. Diffs before/after (without
        // yet bumping UpdatedAtUtc) so a Save that didn't actually change
        // anything doesn't touch the DB or pollute change history.
        public void CommitEdit(ProjectionItemViewModel target, ProjectionItemViewModel scratch)
        {
            var before = target.ToModel();

            target.Name = scratch.Name;
            target.TransactionDate = scratch.TransactionDate;
            target.Periodicity = scratch.Periodicity;
            target.MatchText = scratch.MatchText;

            if (scratch.Deposit.HasValue)
            {
                target.Deposit = scratch.Deposit;
            }
            else
            {
                target.Payment = scratch.Payment;
                if (!scratch.Payment.HasValue)
                    target.Deposit = null;
            }

            if (JsonSerializer.Serialize(before) == JsonSerializer.Serialize(target.ToModel()))
                return;

            target.UpdatedAtUtc = DateTime.UtcNow;
            var after = target.ToModel();

            _itemRepo.Update(after);
            _changeHistory.Record(_account.Id, ChangeEntityType.ProjectionItem, after.Id, ChangeType.Modified, before, after, $"Edited '{after.Name}' — {ChangeDescriber.DescribeProjectionItemDiff(before, after)}");
            _redoStack.Clear();
            _statusVM?.Report($"Saved '{after.Name}'.", busy: false, StatusSeverity.Info);
        }

        [RelayCommand(CanExecute = nameof(CanDeleteOrPushForward))]
        public async Task DeleteTransaction(ProjectionItemViewModel? transaction)
        {
            if (transaction == null)
                return;

            string amountText = transaction.Deposit.HasValue
                ? $"deposit of {transaction.Deposit:C}"
                : transaction.Payment.HasValue
                    ? $"payment of {transaction.Payment:C}"
                    : "amount of $0";

            var confirmed = await _dialogService.ConfirmAsync(
                $"Are you sure you want to delete '{transaction.Name}' {amountText} on {transaction.TransactionDate:d}?",
                "Confirm Delete");

            if (confirmed)
            {
                var before = transaction.ToModel();
                _itemRepo.Delete(transaction.Id);
                _changeHistory.Record(_account.Id, ChangeEntityType.ProjectionItem, transaction.Id, ChangeType.Deleted, before, after: null, $"Deleted '{transaction.Name}'");
                _redoStack.Clear();

                _transactions.Remove(transaction);

                // Don't rely on the grid's own SelectedItem-on-removal
                // fixup running before the editor popover's RequestClose
                // handler reads SelectedTransaction right after this —
                // clear it explicitly so that handler's "fall back to
                // today's row" logic doesn't try to re-focus a row that no
                // longer exists.
                if (ReferenceEquals(SelectedTransaction, transaction))
                    SelectedTransaction = null;
            }
        }

        // Called after any popover closes (Save/OK/Cancel/Escape alike) so
        // keyboard focus lands back on the grid instead of nowhere —
        // restoring to whatever's already selected, or falling back to
        // whichever row's date is closest to today (ties broken by the
        // grid's own display order) so arrow-key navigation always has a
        // sensible place to resume from. Deliberately NOT an exact
        // TransactionDate == today match with a "first row" fallback
        // behind it — the grid sorts newest/furthest-future first, so on
        // the (common) day nothing is dated exactly today, "first row"
        // silently meant "the single furthest-future-dated row in the
        // whole list," which is exactly the opposite of useful here.
        public void FocusGridAfterPopoverClosed()
        {
            var today = DateTime.Today;
            var target = SelectedTransaction
                ?? _transactions
                    .OrderBy(t => t, s_comparer)
                    .OrderBy(t => Math.Abs((t.TransactionDate.Date - today).Ticks))
                    .FirstOrDefault();

            if (target is not null)
                FocusTransaction(target);
        }

        // Selects and scrolls to a row — used by Find to jump to a result,
        // and after adding a new item, so it's immediately visible. The
        // grid is FullRow-selection and read-only (no in-cell editing), so
        // giving the row/grid itself keyboard focus is enough for arrow-key
        // navigation to resume from here — unlike WPF's DataGrid, there's no
        // need to drill down to an individual DataGridCell.
        public void FocusTransaction(ProjectionItemViewModel item)
        {
            if (item is null)
                return;

            SelectedTransaction = item;

            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_viewRef == null || !_viewRef.TryGetTarget(out var fe))
                    return;

                if (fe.FindName("TransactionsGrid") is not DataGrid dg)
                    return;

                dg.SelectedItem = item;
                dg.ScrollIntoView(item, null);

                // ScrollIntoView only schedules the layout pass that would
                // realize this row — the grid virtualizes rows, so without
                // forcing that layout to actually happen right now, the
                // row's container usually doesn't exist yet to focus below.
                dg.UpdateLayout();
                dg.Focus(FocusState.Programmatic);
            });
        }

        [RelayCommand(CanExecute = nameof(CanDeleteOrPushForward))]
        public void PushForward(ProjectionItemViewModel? transaction)
        {
            if (transaction == null)
                return;

            var before = transaction.ToModel();
            ProjectionRescheduler.Advance(transaction, Transactions, realizedAmount: null);
            transaction.UpdatedAtUtc = DateTime.UtcNow;
            var after = transaction.ToModel();

            _itemRepo.Update(after);
            _changeHistory.Record(_account.Id, ChangeEntityType.ProjectionItem, transaction.Id, ChangeType.Rescheduled, before, after, $"Pushed '{transaction.Name}' forward — {ChangeDescriber.DescribeProjectionItemDiff(before, after)}");
            _redoStack.Clear();
        }

        // Manually overrides a real bank transaction's running balance (the
        // "Correct Balance" popover) — e.g. when Plaid's live balance and
        // its transaction feed have drifted apart and you've confirmed the
        // true post-transaction balance some other way (your bank's own
        // website, a statement, etc.). Writes directly to BankTransactions
        // via the one column UpdateRunningBalance touches, then does a full
        // reload so ResortAndRecalculate's checkpoint-aware backward walk
        // picks this row up as a trusted anchor and recomputes every
        // earlier real transaction's balance from it.
        //
        // Deliberately NOT recorded in change history — there's no
        // BankTransaction case in ChangeEntityType, and this follows the
        // same precedent as the other direct BankTransaction/Account
        // mutations in this app (Plaid sync's own balance/transaction
        // writes), none of which go through change history either. Not
        // undoable via Ctrl+Z; the popover warns about this before saving.
        public void CorrectBalance(ProjectionItemViewModel transaction, decimal newBalance)
        {
            if (transaction.BankTransactionId is not { } bankTransactionId)
                return;

            _bankRepo.UpdateRunningBalance(bankTransactionId, newBalance);
            ReloadTransactionsFromDatabase();
            _statusVM?.Report($"Corrected balance for '{transaction.Name}' to {newBalance:C2}.", busy: false, StatusSeverity.Info);
        }

        [RelayCommand]
        public void Undo()
        {
            var latest = _changeHistory.GetAllNewestFirst(_account.Id).FirstOrDefault();
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
            // LoadAllRows() below rebuilds every row as a brand-new
            // ProjectionItemViewModel instance, so SelectedTransaction's
            // current reference is about to go dangling even if the same
            // logical row still exists afterward — capture enough to
            // re-find it by identity (below), not by reference.
            var previousSelection = SelectedTransaction;
            var hadKeyboardFocus = IsGridKeyboardFocusWithin();

            foreach (var t in _transactions)
                t.PropertyChanged -= Transaction_PropertyChanged;
            _transactions.CollectionChanged -= Transactions_CollectionChanged;

            LoadAllRows();

            foreach (var t in _transactions)
                t.PropertyChanged += Transaction_PropertyChanged;
            _transactions.CollectionChanged += Transactions_CollectionChanged;

            ResortAndRecalculate();

            var restored = previousSelection is null
                ? null
                : _transactions.FirstOrDefault(t => SameRow(t, previousSelection));
            if (restored is not null)
            {
                if (hadKeyboardFocus)
                    FocusTransaction(restored);
                else
                    SelectedTransaction = restored;
            }
        }

        // Identifies "the same logical row" across a full reload, where
        // every row is rebuilt as a new object — by Id for an editable
        // item, or by BankTransactionId for a real (read-only) bank row,
        // since a fresh ProjectionItemViewModel never reuses the previous
        // instance's reference.
        private static bool SameRow(ProjectionItemViewModel a, ProjectionItemViewModel b) =>
            a.IsReadOnly == b.IsReadOnly
            && (a.IsReadOnly ? a.BankTransactionId == b.BankTransactionId : a.Id == b.Id);

        // Whether keyboard focus currently belongs to the grid or something
        // inside it — used to decide whether a disruptive refresh/resort
        // should pull focus back in afterward, or just quietly restore the
        // selection without yanking focus away from wherever the user
        // actually is (e.g. mid-edit in the item editor popover). WinUI has
        // no FrameworkElement.IsKeyboardFocusWithin — walk up from whatever
        // currently holds focus instead.
        private bool IsGridKeyboardFocusWithin()
        {
            if (_viewRef is null || !_viewRef.TryGetTarget(out var fe) || fe.XamlRoot is null)
                return false;

            if (fe.FindName("TransactionsGrid") is not DataGrid dg)
                return false;

            var current = FocusManager.GetFocusedElement(fe.XamlRoot) as DependencyObject;
            while (current is not null)
            {
                if (ReferenceEquals(current, dg))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // Re-reads the account row from the database and refreshes display
        // state. Used after a background Plaid sync updates the real
        // starting balance, so the UI doesn't go stale.
        public void RefreshAccountFromDatabase()
        {
            var fresh = _accountRepo.GetById(_account.Id);
            if (fresh is null)
                return;

            _account.Name = fresh.Name;
            _account.StartingBalance = fresh.StartingBalance;
            _account.StartingBalanceAsOf = fresh.StartingBalanceAsOf;
            _account.PlaidItemId = fresh.PlaidItemId;
            _account.PlaidAccountId = fresh.PlaidAccountId;

            ResortAndRecalculate();
        }

        public void SetViewReference(FrameworkElement view)
        {
            _viewRef = new WeakReference<FrameworkElement>(view);
        }

        [RelayCommand]
        public void ToggleSearch()
        {
            IsSearchOpen = !IsSearchOpen;
        }

        private void FocusSearch()
        {
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_viewRef != null && _viewRef.TryGetTarget(out var fe))
                {
                    if (fe.FindName("SearchTextBox") is TextBox tb)
                    {
                        tb.Focus(FocusState.Programmatic);
                        tb.SelectAll();
                    }
                }
            });
        }

        partial void OnIsSearchOpenChanged(bool value)
        {
            if (value)
                FocusSearch();
        }

        // Real bank transactions and projected items get their Balance
        // computed in two separate passes:
        //   - Real rows are walked backward through calendar-date groups
        //     (grouped since Plaid gives no reliable time-of-day to order
        //     same-day rows honestly), starting from today's live balance.
        //     Any date-group containing a known balance — Plaid's own
        //     running_balance, or a manual correction via the Correct
        //     Balance popover (BankTransactionRepository.UpdateRunningBalance)
        //     — is a trusted checkpoint: it resets the running anchor for
        //     every earlier group, instead of every real row being
        //     estimated purely from today's balance regardless of how far
        //     back it is. Never influenced by projected items.
        //   - Projected rows: a forward-looking estimate, starting from the
        //     same current balance and advanced only through other
        //     projected items in date order.
        private void ResortAndRecalculate()
        {
            if (_isRecalculating)
                return;

            // The final reorder below can move rows around even when
            // nothing about the actual order changed, which can tear down
            // and rebuild the grid's row containers and lose whichever row
            // held real keyboard focus. Restore it afterward, same object
            // reference (a reorder doesn't replace any row's object, just
            // repositions them), but only if the grid actually had focus
            // beforehand — never steal focus into the grid on behalf of a
            // background recalculation the user didn't ask for.
            var previousSelection = SelectedTransaction;
            var hadKeyboardFocus = IsGridKeyboardFocusWithin();

            _isRecalculating = true;
            try
            {
                // Segmented by "checkpoint" dates — any date-group
                // containing a known-balance row (Plaid-reported, or
                // manually set via the Correct Balance popover) is trusted
                // as-is and RESETS the running anchor for every earlier
                // group, rather than every real row being estimated purely
                // from today's live balance regardless of how far back it
                // is. This is what makes a manual correction on an old
                // transaction actually ripple backward through everything
                // older than it — not just change that one row.
                var realGroupsByDateDescending = _transactions
                    .Where(t => t.IsReadOnly && !t.IsSyntheticPendingGap)
                    .GroupBy(t => t.TransactionDate.Date)
                    .OrderByDescending(g => g.Key)
                    .ToList();

                // Detected live, every pass, as a side effect of this same
                // walk — the discrepancy between "what today's live balance
                // implies this date-group's balance should be, walking
                // backward through only the real transactions actually
                // known in between" and what the FIRST checkpoint we reach
                // (Plaid-reported or manually corrected) actually says.
                // Null until that first checkpoint is hit; stays at
                // whatever it was set to after that, since only the
                // most-recent checkpoint's reconciliation against "today"
                // is meaningful here — older checkpoints further back just
                // continue resetting the anchor for their own earlier
                // groups, same as before. This is what makes a manual
                // correction (or ordinary Plaid sync latency) that no
                // longer reconciles with the live balance get explained by
                // a "Pending transaction(s)" placeholder instead of
                // silently vanishing into an unrelated row's balance.
                decimal? pendingGapAmount = null;

                // Every date-group walked BEFORE the first checkpoint is
                // found gets its Balance assigned from the raw live
                // StartingBalance, with no idea yet that a gap exists — the
                // gap is only known once the walk actually reaches that
                // checkpoint, which can be several groups further back.
                // Collected here so they can be corrected retroactively
                // once the gap is actually known (below), rather than
                // silently keeping their un-adjusted (wrong) Balance.
                var groupsBeforeFirstCheckpoint = new List<IGrouping<DateTime, ProjectionItemViewModel>>();

                var runningReal = _account.StartingBalance;
                foreach (var dateGroup in realGroupsByDateDescending)
                {
                    var checkpoint = dateGroup.FirstOrDefault(t => t.HasKnownBalance);
                    if (checkpoint is not null)
                    {
                        if (pendingGapAmount is null)
                            pendingGapAmount = runningReal - checkpoint.Balance;

                        // Trusted fact — becomes the anchor for every
                        // date-group older than it. Still (re-)applied to
                        // every row in this date-group, not just the
                        // checkpoint row itself: Plaid gives no reliable
                        // time-of-day, so same-day rows always share one
                        // end-of-date balance, same as the estimated case
                        // below — otherwise an unknown-balance sibling of a
                        // same-day checkpoint would never get a Balance
                        // assigned at all.
                        runningReal = checkpoint.Balance;
                    }
                    else if (pendingGapAmount is null)
                    {
                        groupsBeforeFirstCheckpoint.Add(dateGroup);
                    }

                    foreach (var t in dateGroup)
                        t.Balance = runningReal; // balance as of end of this date

                    // Step back to the balance as it stood before this date's
                    // transactions, i.e. after the previous date's.
                    var netEffect = dateGroup.Sum(t => (t.Deposit ?? 0m) - (t.Payment ?? 0m));
                    runningReal -= netEffect;
                }

                // Adds, updates in place, or removes the synthetic
                // placeholder based on what the walk above just found — a
                // null pendingGapAmount (no checkpoint anywhere in the
                // known history) means there's nothing to compare the live
                // balance against, so no placeholder either.
                SyncPendingGapPlaceholder(pendingGapAmount ?? 0m);

                // Retroactively correct every row assigned before the gap
                // was known — e.g. a real transaction dated between today
                // and the checkpoint should reflect that the pending
                // transaction hasn't posted yet either, the same as today's
                // own balance does.
                if (pendingGapAmount is { } gap && Math.Abs(gap) >= 0.01m)
                {
                    foreach (var dateGroup in groupsBeforeFirstCheckpoint)
                        foreach (var t in dateGroup)
                            t.Balance -= gap;
                }

                // Keep EffectiveSortDate in sync with the item's own date
                // for every unmatched row — PushForward/edits mutate
                // TransactionDate directly on the live object without a
                // full reload, and nothing else keeps this in sync.
                // Matched items are left alone; ApplyConfirmedMatches owns
                // theirs (borrowed from the real transaction they're
                // paired with), set once at load time.
                foreach (var t in _transactions.Where(t => !t.MatchGroupId.HasValue))
                    t.EffectiveSortDate = t.TransactionDate;

                // Unmatched projections dated on/before the latest real
                // bank transaction haven't posted at all yet despite being
                // "due" by date — sorting/balancing them in raw date order
                // interleaves them between real rows and produces an
                // unexplained dip-then-recover in the Balance column that
                // doesn't reflect how the balance actually moved. Pin them
                // to sort (and be walked for the balance below) as a group
                // directly above every real transaction instead, in order
                // among themselves by actual date — while real activity
                // stays contiguous below them. Also flagged for a distinct
                // warning highlight: these are the ones worth double-
                // checking — did the bill actually get paid, or was it
                // forgotten?
                // Excludes the synthetic pending-gap placeholder — it's
                // dated "now" by construction, and counting it here would
                // make every cycle with an open gap treat "now" as the
                // latest real activity date, over-triggering this warning
                // on projections that aren't actually overdue relative to
                // genuine bank activity.
                var latestRealDate = _transactions
                    .Where(t => t.IsReadOnly && !t.IsSyntheticPendingGap)
                    .Select(t => t.TransactionDate.Date)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();

                var pendingBankActivity = _transactions
                    .Where(t => !t.IsReadOnly && !t.MatchGroupId.HasValue && t.TransactionDate.Date <= latestRealDate)
                    .OrderBy(t => t.TransactionDate)
                    .ToList();

                foreach (var t in _transactions.Where(t => !t.IsReadOnly))
                    t.IsPendingBankActivity = false;

                for (var i = 0; i < pendingBankActivity.Count; i++)
                {
                    pendingBankActivity[i].IsPendingBankActivity = true;
                    pendingBankActivity[i].EffectiveSortDate = latestRealDate.AddDays(1).AddSeconds(i);
                }

                // Walked in EffectiveSortDate order (not raw TransactionDate)
                // so the Balance column reads correctly against the row
                // order actually on screen — including the pinned pending
                // items above, applied first/closest to the real balance.
                var projectedAscending = _transactions
                    .Where(t => !t.IsReadOnly)
                    .OrderBy(t => t.EffectiveSortDate)
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
                var end = now.AddDays(30);

                // Only projection items can be the "lowest" point — this is
                // a warning about projected activity, not a historical fact
                // about what the real balance already did. Deliberately NO
                // lower bound on TransactionDate: an overdue unmatched
                // projection is never "in the past" as far as this app is
                // concerned — it's pinned to sort and balance-walk as
                // upcoming (EffectiveSortDate, above) regardless of how
                // stale its raw date is, so excluding it here just because
                // its date fell behind "now" would be inconsistent with
                // that. The only real bound is how far forward to bother
                // looking — there's no value in flagging a projected low
                // point months from now when nothing between here and
                // there is settled yet.
                var candidates = ordered
                    .Where(t => !t.IsReadOnly && t.TransactionDate.Date <= end)
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

                // WinUI has no live-shaping CollectionView to lean on for
                // auto-resorting, so the collection itself is kept in
                // sorted order directly: move each row into the position
                // `ordered` (computed above, via the same comparer) says it
                // belongs in. ObservableCollection.Move fires a Move
                // CollectionChanged notification, which re-enters
                // Transactions_CollectionChanged -> ResortAndRecalculate()
                // — safely a no-op thanks to the _isRecalculating guard at
                // the top of this method.
                ReorderTransactionsToMatch(ordered);
            }
            finally
            {
                _isRecalculating = false;
            }

            if (hadKeyboardFocus && previousSelection is not null && _transactions.Contains(previousSelection))
                FocusTransaction(previousSelection);
        }

        // Minimal-moves in-place reorder: walk the target order left to
        // right, and move whatever's currently in that position into place
        // if it's not already there. Quadratic in the worst case (IndexOf
        // per row), but the row counts here are small enough (hundreds, not
        // millions) that this is never a practical concern.
        private void ReorderTransactionsToMatch(List<ProjectionItemViewModel> ordered)
        {
            for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var item = ordered[targetIndex];
                var currentIndex = _transactions.IndexOf(item);
                if (currentIndex != targetIndex)
                    _transactions.Move(currentIndex, targetIndex);
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

            // Deferred, not called inline: we're still synchronously inside
            // _transactions' own CollectionChanged dispatch for whatever
            // Add/Remove got us here (e.g. CommitNewItem's _transactions.Add),
            // and ResortAndRecalculate ultimately mutates this same
            // collection again (ReorderTransactionsToMatch's Move calls,
            // SyncPendingGapPlaceholder's Add/Remove) — ObservableCollection
            // throws ("Cannot change ObservableCollection during a
            // CollectionChanged event") if you do that reentrantly. Running
            // it on the next dispatcher tick instead means it's never
            // nested inside the triggering mutation's own event raise.
            _dispatcherQueue.TryEnqueue(ResortAndRecalculate);
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

                // The synthetic pending-gap placeholder is guaranteed to
                // sort directly between every projection item (above it)
                // and every real bank transaction (below it), regardless
                // of its own EffectiveSortDate ("now") — date comparisons
                // alone can't guarantee this: an overdue pinned projection,
                // or an ordinary projection due today, can date-compare on
                // either side of "now" depending on exactly when "now" is.
                // Only short-circuits when ONE side is the placeholder, so
                // this can never disturb the matched-pair adjacency logic
                // below for two ordinary rows (a matched projection item
                // deliberately shares its paired real transaction's date
                // and sorts next to it, wherever that falls chronologically).
                if (a.IsSyntheticPendingGap != b.IsSyntheticPendingGap)
                {
                    if (a.IsSyntheticPendingGap)
                        return b.IsReadOnly ? -1 : 1;
                    return a.IsReadOnly ? 1 : -1;
                }

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
            // Real bank-transaction rows are read-only — nothing to push
            // forward or delete.
            return transaction is not null && !transaction.IsReadOnly;
        }
    }
}
