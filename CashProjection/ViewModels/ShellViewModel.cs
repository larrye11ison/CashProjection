using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Caliburn.Micro;

namespace CashProjection.ViewModels
{
    public class ShellViewModel : Conductor<object>, IGuardClose
    {
        private WeakReference<FrameworkElement>? _viewRef;

        public ShellViewModel()
        {
            AccountVM = new AccountProjectionViewModel();
            ActivateItemAsync(AccountVM);

            // Commands
            FocusInitialBalanceCommand = new RelayCommand(_ => FocusInitialBalance());
            SaveCommand = new RelayCommand(_ => Save());
            FindCommand = new RelayCommand(_ => OpenFind());

            SearchResults = new BindableCollection<SearchResult>();
        }

        public AccountProjectionViewModel AccountVM { get; }
        public ICommand FocusInitialBalanceCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand FindCommand { get; }

        // Find overlay state
        private bool _isFindOpen;
        public bool IsFindOpen
        {
            get => _isFindOpen;
            set
            {
                if (_isFindOpen != value)
                {
                    _isFindOpen = value;
                    NotifyOfPropertyChange(() => IsFindOpen);
                }
            }
        }

        private string _findText = string.Empty;
        public string FindText
        {
            get => _findText;
            set
            {
                if (_findText != value)
                {
                    _findText = value;
                    NotifyOfPropertyChange(() => FindText);
                    UpdateSearchResults();
                }
            }
        }

        public BindableCollection<SearchResult> SearchResults { get; }
        private SearchResult? _selectedSearchResult;
        public SearchResult? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set
            {
                if (!Equals(_selectedSearchResult, value))
                {
                    _selectedSearchResult = value;
                    NotifyOfPropertyChange(() => SelectedSearchResult);
                }
            }
        }

        public override Task<bool> CanCloseAsync(CancellationToken cancellationToken)
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
                    return Task.FromResult(false);

                if (result == MessageBoxResult.Yes)
                    AccountVM.Save();
            }

            return Task.FromResult(true);
        }

        protected override void OnViewReady(object view)
        {
            base.OnViewReady(view);
            if (view is FrameworkElement fe)
                _viewRef = new WeakReference<FrameworkElement>(fe);
        }

        public void FocusInitialBalance() => AccountVM?.FocusInitialBalance();

        public void AddNew() => AccountVM?.AddNew();

        public void Save() => AccountVM.Save();

        // Open/close/confirm find
        public void OpenFind()
        {
            IsFindOpen = true;
            FindText = string.Empty;
            SearchResults.Clear();
            SelectedSearchResult = null;

            // Defer to UI thread so the overlay is measured/visible before focusing
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(FocusFindTextBox)
            );
        }

        public void CancelFind()
        {
            IsFindOpen = false;
        }

        public void ConfirmFind()
        {
            var target = SelectedSearchResult?.Item ?? SearchResults.FirstOrDefault()?.Item;
            if (target is null)
                return;

            IsFindOpen = false;
            AccountVM.FocusTransaction(target);
        }

        // Handle Enter/Escape/Up/Down while typing or anywhere in the overlay
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
            public SearchResult(TransactionItemViewModel item, int hits)
            {
                Item = item;
                Hits = hits;
                Display = $"{item.TransactionDate:d}  —  {item.Name}";
            }

            public TransactionItemViewModel Item { get; }
            public int Hits { get; }
            public string Display { get; }
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Func<object?, bool>? _canExecute;
            private readonly Action<object?> _execute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() =>
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
