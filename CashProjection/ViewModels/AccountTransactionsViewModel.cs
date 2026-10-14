using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Data.Repositories;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // Read-only balance + transaction history for an account that doesn't
    // carry projections (money market, savings, a CD, a line of credit,
    // ...). Deliberately has no edit/push-forward/match commands — unlike
    // AccountProjectionViewModel, this is just a mirror of what Plaid
    // reports, nothing here is ever written back.
    public sealed partial class AccountTransactionsViewModel : ObservableObject
    {
        private readonly Account _account;
        private readonly BankTransactionRepository _bankRepo;

        public AccountTransactionsViewModel(Account account, BankTransactionRepository bankRepo)
        {
            _account = account;
            _bankRepo = bankRepo;
            Transactions = new ObservableCollection<BankTransactionRow>();
            Refresh();
        }

        public string AccountName => _account.Name;

        public string BalanceDisplay =>
            _account.StartingBalanceAsOf.HasValue
                ? $"Current balance: {_account.StartingBalance:C2} (as of {_account.StartingBalanceAsOf.Value.ToLocalTime():g}, from Plaid)"
                : $"Current balance: {_account.StartingBalance:C2} (not yet synced from Plaid)";

        public ObservableCollection<BankTransactionRow> Transactions { get; }

        // Call after a sync so the balance header and grid pick up fresh
        // data without needing to rebuild the whole view model.
        public void Refresh()
        {
            OnPropertyChanged(nameof(BalanceDisplay));

            Transactions.Clear();

            // This institution doesn't populate Plaid's running_balance
            // field, so compute it client-side the same way the checking
            // grid does. Rows come back newest-first; _account.StartingBalance
            // is the current balance (i.e. the balance after the newest
            // row), so walk newest-to-oldest, undoing each transaction's
            // effect (Plaid convention: positive amount = money out) to
            // get the balance as of just after the next-older row.
            var runningBalance = _account.StartingBalance;
            foreach (var txn in _bankRepo.GetByAccount(_account.Id))
            {
                Transactions.Add(new BankTransactionRow(txn, runningBalance));
                runningBalance += txn.Amount;
            }
        }

        public sealed class BankTransactionRow
        {
            public BankTransactionRow(BankTransaction txn, decimal runningBalance)
            {
                Date = txn.TxnDate;
                Description = txn.Description;
                Amount = txn.Amount;
                Pending = txn.Pending;
                RunningBalance = runningBalance;
            }

            public DateTime Date { get; }
            public string Description { get; }
            public decimal Amount { get; }
            public bool Pending { get; }
            public decimal RunningBalance { get; }
        }
    }
}
