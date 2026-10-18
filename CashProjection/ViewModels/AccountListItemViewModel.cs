using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // Row for the account-switcher dropdown. Wraps an Account so the list
    // can show a live balance and an unseen-transactions indicator without
    // Account itself (a plain DB row model) needing to be observable.
    public sealed partial class AccountListItemViewModel : ObservableObject
    {
        public AccountListItemViewModel(Account account)
        {
            Account = account;
        }

        public Account Account { get; }

        public int Id => Account.Id;

        public string Name => Account.Name;

        [ObservableProperty]
        private string _balanceDisplay = string.Empty;

        [ObservableProperty]
        private bool _hasUnseenTransactions;

        public void UpdateBalance(decimal balance) => BalanceDisplay = balance.ToString("C2");
    }
}
