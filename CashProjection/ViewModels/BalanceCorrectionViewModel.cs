using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashProjection.ViewModels
{
    // Backs the "Correct Balance" popover — a deliberate manual override of
    // a single real bank transaction's running balance, for when Plaid's
    // live balance and its transaction feed have drifted apart (see
    // ProjectionItemViewModel.CreatePendingGapPlaceholder for the automatic
    // detection of that) and you've confirmed the true balance some other
    // way. Only the balance itself is editable — nothing else about the
    // transaction (name, date, amount) can be changed here, since those are
    // Plaid-reported facts about what actually happened; only the resulting
    // balance is ever in question.
    public sealed partial class BalanceCorrectionViewModel : ObservableObject
    {
        private readonly AccountProjectionViewModel _accountVM;
        private readonly ProjectionItemViewModel _target;

        public event Action? RequestClose;

        [ObservableProperty]
        private decimal _newBalance;

        public string TransactionName => _target.PrimaryDisplayName;
        public DateTime TransactionDate => _target.TransactionDate;
        public decimal? Deposit => _target.Deposit;
        public decimal? Payment => _target.Payment;
        public decimal CurrentBalance => _target.Balance;

        public BalanceCorrectionViewModel(AccountProjectionViewModel accountVM, ProjectionItemViewModel target)
        {
            _accountVM = accountVM;
            _target = target;
            NewBalance = target.Balance;
        }

        [RelayCommand]
        public void Save()
        {
            _accountVM.CorrectBalance(_target, NewBalance);
            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void Cancel() => RequestClose?.Invoke();
    }
}
