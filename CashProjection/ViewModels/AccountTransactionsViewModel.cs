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

        public ObservableCollection<BankTransactionRow> Transactions { get; }

        // Call after a sync so the grid picks up fresh data without needing
        // to rebuild the whole view model. The account's balance itself is
        // now shown in the shared shell header (ShellViewModel), sourced
        // directly from the Account row rather than through here.
        public void Refresh()
        {
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
            private readonly BankTransaction _txn;

            public BankTransactionRow(BankTransaction txn, decimal runningBalance)
            {
                _txn = txn;
                Date = txn.TxnDate;
                Amount = txn.Amount;
                Pending = txn.Pending;
                RunningBalance = runningBalance;
            }

            public DateTime Date { get; }
            public string Description => _txn.Description;
            public decimal Amount { get; }
            public bool Pending { get; }
            public decimal RunningBalance { get; }

            // BankTransaction.PrimaryDisplayName / HasDistinctVendorName is
            // the one shared rule for this — see there.
            public bool HasDistinctVendorName => _txn.HasDistinctVendorName;
            public string PrimaryDisplayName => _txn.PrimaryDisplayName;
        }
    }
}
