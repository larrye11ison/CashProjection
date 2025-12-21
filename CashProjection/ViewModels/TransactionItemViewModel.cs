using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // ViewModel wrapper around the pure Transaction model
    public sealed partial class TransactionItemViewModel : ObservableObject
    {
        private readonly Transaction _model;

        [ObservableProperty]
        private decimal _balance;

        [ObservableProperty]
        private bool _isLowestNearNow;

        public TransactionItemViewModel(Transaction model)
        {
            _model = model;
        }

        // Expose the underlying model if needed by services
        public Transaction Model => _model;

        public string Name
        {
            get => _model.Name;
            set
            {
                if (_model.Name != value)
                {
                    _model.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime TransactionDate
        {
            get => _model.TransactionDate;
            set
            {
                if (_model.TransactionDate != value)
                {
                    _model.TransactionDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public decimal? Deposit
        {
            get => _model.Deposit;
            set
            {
                if (_model.Deposit != value)
                {
                    // Preserve prior UX: 0 nullifies deposit and sets payment=0 (so no effect on balance)
                    if (value == 0)
                    {
                        _model.Deposit = null;
                        _model.Payment = 0;
                        OnPropertyChanged(nameof(Payment));
                    }
                    else
                    {
                        _model.Deposit = value;
                        if (value.HasValue)
                        {
                            _model.Payment = null;
                            OnPropertyChanged(nameof(Payment));
                        }
                    }
                    OnPropertyChanged();
                }
            }
        }

        public decimal? Payment
        {
            get => _model.Payment;
            set
            {
                if (_model.Payment != value)
                {
                    _model.Payment = value;
                    if (value.HasValue)
                    {
                        _model.Deposit = null;
                        OnPropertyChanged(nameof(Deposit));
                    }
                    OnPropertyChanged();
                }
            }
        }

        public Periodicity Periodicity
        {
            get => _model.Periodicity;
            set
            {
                if (_model.Periodicity != value)
                {
                    _model.Periodicity = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
