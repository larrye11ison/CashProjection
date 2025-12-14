using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Caliburn.Micro;
using CashProjection.Models;
using CashProjection.Services;

namespace CashProjection.ViewModels
{
    public class AccountProjectionViewModel : Screen
    {
        private static readonly TransactionComparer s_comparer = new();
        private string _accountName = "My Account";
        private decimal _initialBalance = 5000m;

        // Re-entrancy guard for ResortAndRecalculate
        private bool _isRecalculating;

        // Search panel state (Caliburn.Micro friendly)
        private bool _isSearchOpen;

        private string _searchText = string.Empty;
        private ICommand? _toggleSearchCommand;
        private BindableCollection<TransactionItemViewModel> _transactions;
        private WeakReference<FrameworkElement>? _viewRef;

        public AccountProjectionViewModel()
        {
            _transactions = new BindableCollection<TransactionItemViewModel>();
            _transactions.CollectionChanged += Transactions_CollectionChanged;

            // Load saved state if it exists; otherwise load sample data
            var saved = PersistenceService.Load();
            if (saved is not null)
            {
                ApplyState(saved);
            }
            else
            {
                AddSampleTransactions();
            }

            foreach (var t in _transactions)
                t.PropertyChanged += Transaction_PropertyChanged;

            ApplySort();
            ResortAndRecalculate();

            IsDirty = false;
        }

        public string AccountName
        {
            get => _accountName;
            set
            {
                if (_accountName != value)
                {
                    _accountName = value;
                    NotifyOfPropertyChange();
                    MarkDirty();
                }
            }
        }

        public decimal InitialBalance
        {
            get => _initialBalance;
            set
            {
                if (_initialBalance != value)
                {
                    _initialBalance = value;
                    NotifyOfPropertyChange();
                    MarkDirty();
                    ResortAndRecalculate();
                }
            }
        }

        public bool IsDirty { get; private set; }

        // Exposed for binding to the Card Visibility (use BoolToVis in XAML)
        public bool IsSearchOpen
        {
            get => _isSearchOpen;
            set
            {
                if (_isSearchOpen != value)
                {
                    _isSearchOpen = value;
                    NotifyOfPropertyChange();
                    // When opening the search panel, focus the textbox
                    if (_isSearchOpen)
                        FocusSearch();
                }
            }
        }

        // Bound to the search TextBox in the view
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    NotifyOfPropertyChange();
                    // Optionally: trigger filtering here
                }
            }
        }

        // Provide an ICommand for use with KeyBinding (Ctrl+F) in XAML if desired.
        // Caliburn also allows calling the parameterless method ToggleSearch from actions.
        public ICommand ToggleSearchCommand => _toggleSearchCommand ??= new RelayCommand(() => ToggleSearch());

        public BindableCollection<TransactionItemViewModel> Transactions
        {
            get => _transactions;
            set
            {
                if (_transactions != value)
                {
                    _transactions = value;
                    NotifyOfPropertyChange();
                    MarkDirty();
                }
            }
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

        public void FocusInitialBalance()
        {
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (_viewRef != null && _viewRef.TryGetTarget(out var fe))
                    {
                        if (fe.FindName("InitialBalanceTextBox") is TextBox tb)
                        {
                            tb.Focus();
                            tb.SelectAll();
                        }
                    }
                })
            );
        }

        // Focus a specific transaction row and place caret into a useful cell editor
        public void FocusTransaction(TransactionItemViewModel item)
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

                    // Ensure pending edits are committed before changing selection
                    dg.CommitEdit(DataGridEditingUnit.Cell, true);
                    dg.CommitEdit(DataGridEditingUnit.Row, true);

                    // Select item and choose a target column: prefer Deposit, then Payment, else Name
                    dg.SelectedItem = item;
                    var targetColumn = ChooseTargetColumn(dg, item) ?? dg.Columns.FirstOrDefault();
                    if (targetColumn is null)
                    {
                        dg.Focus();
                        return;
                    }

                    // Make the desired cell current and visible
                    dg.CurrentCell = new DataGridCellInfo(item, targetColumn);
                    dg.ScrollIntoView(item, targetColumn);
                    dg.UpdateLayout();

                    // Begin edit so the editing element (TextBox) is in the visual tree
                    dg.BeginEdit();

                    // Try to focus the editing TextBox (or any focusable element) inside the cell
                    var cellContent = targetColumn.GetCellContent(item);
                    if (cellContent != null)
                    {
                        // If currently showing the display template, switch editing for template columns
                        if (FindVisualChild<TextBox>(cellContent) is TextBox editorTb)
                        {
                            editorTb.Focus();
                            editorTb.SelectAll();
                            Keyboard.Focus(editorTb);
                        }
                        else
                        {
                            // Fallback: place keyboard focus on the cell content
                            if (cellContent is Control ctrl)
                            {
                                ctrl.Focus();
                                Keyboard.Focus(ctrl);
                            }
                        }
                    }
                    else
                    {
                        // Last resort: focus the row itself
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

        public void PushForward(TransactionItemViewModel transaction)
        {
            if (transaction == null)
                return;

            if (transaction.Periodicity == Periodicity.TwoWeeksPastLast)
            {
                var view = CollectionViewSource.GetDefaultView(Transactions);
                var lastTransaction = view
                    .OfType<TransactionItemViewModel>()
                    .Where(t => t.Name == transaction.Name && !ReferenceEquals(t, transaction))
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefault();
                if (lastTransaction is not null)
                {
                    transaction.TransactionDate = lastTransaction.TransactionDate.AddDays(14);
                    MarkDirty();
                    // copilot suggests removing this, which I think is bullshit
                    //ResortAndRecalculate();
                    return;
                }
            }

            transaction.TransactionDate = transaction.Periodicity switch
            {
                Periodicity.Monthly => transaction.TransactionDate.AddMonths(1),
                Periodicity.Quarterly => transaction.TransactionDate.AddMonths(3),
                Periodicity.SemiAnnually => transaction.TransactionDate.AddMonths(6),
                Periodicity.Annually => transaction.TransactionDate.AddYears(1),
                _ => transaction.TransactionDate,
            };

            MarkDirty();
            // copilot suggests removing this, which I think is bullshit
            //ResortAndRecalculate();
        }

        public void AddNew()
        {
            var newTransaction = new TransactionItemViewModel(new Transaction
            {
                Name = string.Empty,
                TransactionDate = DateTime.Today,
                Deposit = null,
                Payment = null,
                Periodicity = Periodicity.Monthly
            });

            _transactions.Add(newTransaction);
            MarkDirty();

            // Focus the new transaction's Date field after it's added to the grid
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() =>
                {
                    if (_viewRef == null || !_viewRef.TryGetTarget(out var fe))
                        return;

                    if (fe.FindName("TransactionsGrid") is not DataGrid dg)
                        return;

                    // Select the new item and focus the Date column
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

        public void DeleteTransaction(TransactionItemViewModel transaction)
        {
            if (transaction == null)
                return;

            // Format the amount (either deposit or payment)
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
                _transactions.Remove(transaction);
                MarkDirty();
            }
        }

        public void Save()
        {
            PersistenceService.Save(this);
            IsDirty = false;
        }

        // Caliburn-friendly action method (can be invoked from cal:Message.Attach or wired to UI)
        public void ToggleSearch()
        {
            IsSearchOpen = !IsSearchOpen;
        }

        protected override void OnViewReady(object view)
        {
            base.OnViewReady(view);
            if (view is FrameworkElement fe)
                _viewRef = new WeakReference<FrameworkElement>(fe);
        }

        private static DataGridColumn? ChooseTargetColumn(
                    DataGrid dg,
                    TransactionItemViewModel item
                )
        {
            if (item.Deposit.HasValue)
                return GetColumnByHeader(dg, "Deposit");
            if (item.Payment.HasValue)
                return GetColumnByHeader(dg, "Payment");

            // Default to Name column for keyboard navigation across the row
            return GetColumnByHeader(dg, "Name") ?? dg.Columns.FirstOrDefault();
        }

        private static T? FindVisualChild<T>(DependencyObject parent)
                    where T : DependencyObject
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

        private void AddSampleTransactions()
        {
            // Load sample data from SampleData.json (fallback in PersistenceService already handles this)
            var sample = PersistenceService.Load();

            if (sample is null)
            {
                // If no sample data exists, initialize with empty state
                InitialBalance = 0m;
                AccountName = "My Account";
                return;
            }

            AccountName = sample.AccountName;
            InitialBalance = sample.InitialBalance;

            var vms = sample.Transactions.Select(t => new TransactionItemViewModel(new Transaction
            {
                Name = t.Name,
                TransactionDate = t.TransactionDate,
                Deposit = t.Deposit,
                Payment = t.Payment,
                Periodicity = t.Periodicity
            }));

            _transactions.AddRange(vms);
            ResortAndRecalculate();
            IsDirty = false;
        }

        // Sort by date asc, then payments before deposits, then name and amount for determinism.
        private void ApplySort()
        {
            var view = CollectionViewSource.GetDefaultView(Transactions);

            if (
                view is IEditableCollectionView editable
                && (editable.IsEditingItem || editable.IsAddingNew)
            )
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
                        nameof(TransactionItemViewModel.TransactionDate),
                        ListSortDirection.Ascending
                    )
                );
            }

            if (view is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
            {
                live.IsLiveSorting = true;
                live.LiveSortingProperties?.Clear();
                live.LiveSortingProperties?.Add(nameof(TransactionItemViewModel.TransactionDate));
                live.LiveSortingProperties?.Add(nameof(TransactionItemViewModel.Payment));
                live.LiveSortingProperties?.Add(nameof(TransactionItemViewModel.Deposit));
            }
        }

        private void ApplyState(AccountState state)
        {
            _accountName = state.AccountName;
            NotifyOfPropertyChange(nameof(AccountName));

            _initialBalance = state.InitialBalance;
            NotifyOfPropertyChange(nameof(InitialBalance));

            _transactions.Clear();
            var vms = state
                .Transactions.Select(s => new Transaction
                {
                    Name = s.Name,
                    TransactionDate = s.TransactionDate,
                    Deposit = s.Deposit,
                    Payment = s.Payment,
                    Periodicity = s.Periodicity,
                })
                .Select(m => new TransactionItemViewModel(m));

            _transactions.AddRange(vms);

            IsDirty = false;
        }

        // Focus the search TextBox when opening the panel
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

        private void MarkDirty() => IsDirty = true;

        private void ResortAndRecalculate()
        {
            // Re-entrancy guard: prevent cascading recalculations
            if (_isRecalculating)
                return;

            _isRecalculating = true;
            try
            {
                System.Diagnostics.Debug.WriteLine("Resorting and recalculating balances...");

                // Use the base collection and sort it directly with our canonical comparer
                // This avoids CollectionView state issues entirely
                var ordered = _transactions
                    .OrderBy(t => t, s_comparer)
                    .ToList();

                System.Diagnostics.Debug.WriteLine("Recalculating balances...");
                decimal runningBalance = InitialBalance;
                foreach (var t in ordered)
                {
                    if (t.Deposit.HasValue && t.Deposit.Value != 0)
                        runningBalance += t.Deposit.Value;
                    else if (t.Payment.HasValue && t.Payment.Value != 0)
                        runningBalance -= t.Payment.Value;

                    t.Balance = runningBalance;
                }

                var now = DateTime.Now.Date;
                var start = now.AddMonths(-1);
                var end = now.AddMonths(1);

                var candidates = ordered
                    .Where(t => t.TransactionDate.Date >= start && t.TransactionDate.Date <= end)
                    .ToList();
                TransactionItemViewModel? lowest = null;
                if (candidates.Count > 0)
                {
                    var minBal = candidates.Min(t => t.Balance);
                    lowest = candidates.FirstOrDefault(t => t.Balance == minBal);
                }

                foreach (var t in ordered)
                    t.IsLowestNearNow = ReferenceEquals(t, lowest);
            }
            finally
            {
                _isRecalculating = false;
            }
        }

        private void Transaction_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TransactionItemViewModel.Balance))
                return;

            if (
                e.PropertyName
                is nameof(TransactionItemViewModel.Name)
                    or nameof(TransactionItemViewModel.TransactionDate)
                    or nameof(TransactionItemViewModel.Deposit)
                    or nameof(TransactionItemViewModel.Payment)
                    or nameof(TransactionItemViewModel.Periodicity)
            )
            {
                MarkDirty();
                ResortAndRecalculate();
            }
        }

        private void Transactions_CollectionChanged(
            object? sender,
            NotifyCollectionChangedEventArgs e
        )
        {
            if (e.NewItems != null)
                foreach (TransactionItemViewModel t in e.NewItems)
                    t.PropertyChanged += Transaction_PropertyChanged;

            if (e.OldItems != null)
                foreach (TransactionItemViewModel t in e.OldItems)
                    t.PropertyChanged -= Transaction_PropertyChanged;

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var t in _transactions)
                {
                    t.PropertyChanged -= Transaction_PropertyChanged;
                    t.PropertyChanged += Transaction_PropertyChanged;
                }
            }

            MarkDirty();
            ResortAndRecalculate();
        }

        // Minimal RelayCommand for KeyBinding or other ICommand usage
        private sealed class RelayCommand : ICommand
        {
            private readonly System.Func<bool>? _canExecute;
            private readonly System.Action _execute;

            public RelayCommand(System.Action execute, System.Func<bool>? canExecute = null)
            {
                _execute = execute ?? throw new System.ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

            public void Execute(object? parameter) => _execute();

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class TransactionComparer : System.Collections.IComparer, IComparer<TransactionItemViewModel>
        {
            public int Compare(object? x, object? y)
            {
                if (x is TransactionItemViewModel a && y is TransactionItemViewModel b)
                    return Compare(a, b);

                if (ReferenceEquals(x, y))
                    return 0;
                if (x is not TransactionItemViewModel)
                    return -1;
                return 1;
            }

            public int Compare(TransactionItemViewModel? a, TransactionItemViewModel? b)
            {
                if (ReferenceEquals(a, b))
                    return 0;
                if (a is null)
                    return -1;
                if (b is null)
                    return 1;

                int c = a.TransactionDate.CompareTo(b.TransactionDate);
                if (c != 0)
                    return c;

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
    }
}