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

namespace CashProjection.ViewModels
{
    public sealed partial class ShellViewModel : ObservableObject
    {
        private WeakReference<FrameworkElement>? _viewRef;

        [ObservableProperty]
        private bool _isFindOpen;

        [ObservableProperty]
        private string _findText = string.Empty;

        [ObservableProperty]
        private SearchResult? _selectedSearchResult;

        public ShellViewModel()
        {
            AccountVM = new AccountProjectionViewModel();
            SearchResults = new ObservableCollection<SearchResult>();
        }

        public AccountProjectionViewModel AccountVM { get; }

        public ObservableCollection<SearchResult> SearchResults { get; }

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
        public void FocusInitialBalance() => AccountVM?.FocusInitialBalance();

        [RelayCommand]
        public void AddNew() => AccountVM?.AddNew();

        [RelayCommand]
        public void Save() => AccountVM.Save();

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
            public SearchResult(TransactionItemViewModel item, int hits)
            {
                Item = item;
                Hits = hits;
                Display = $"{item.TransactionDate:d}  –  {item.Name}";
            }

            public TransactionItemViewModel Item { get; }
            public int Hits { get; }
            public string Display { get; }
        }
    }
}
