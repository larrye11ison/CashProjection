using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // ViewModel for a transaction item in the UI
    public sealed partial class TransactionItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private DateTime _transactionDate = DateTime.Today;

        [ObservableProperty]
        private decimal? _deposit;

        [ObservableProperty]
        private decimal? _payment;

        [ObservableProperty]
        private Periodicity _periodicity;

        [ObservableProperty]
        private decimal _balance;

        [ObservableProperty]
        private bool _isLowestNearNow;

        // Default constructor for new transactions
        public TransactionItemViewModel()
        {
        }

        // Constructor to load from a model (when loading from disk)
        public TransactionItemViewModel(Transaction model)
        {
            Name = model.Name;
            TransactionDate = model.TransactionDate;
            Deposit = model.Deposit;
            Payment = model.Payment;
            Periodicity = model.Periodicity;
        }

        // Convert to model for saving
        public Transaction ToModel() => new Transaction
        {
            Name = Name,
            TransactionDate = TransactionDate,
            Deposit = Deposit,
            Payment = Payment,
            Periodicity = Periodicity
        };

        // Handle the special business logic for Deposit/Payment mutual exclusion
        partial void OnDepositChanged(decimal? value)
        {
            // Preserve prior UX: 0 nullifies deposit and sets payment=0
            if (value == 0)
            {
                Deposit = null;
                Payment = 0;
            }
            else if (value.HasValue)
            {
                Payment = null;
            }
        }

        partial void OnPaymentChanged(decimal? value)
        {
            if (value.HasValue)
            {
                Deposit = null;
            }
        }
    }
}
