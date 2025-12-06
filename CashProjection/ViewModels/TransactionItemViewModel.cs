using Caliburn.Micro;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // ViewModel wrapper around the pure Transaction model
    public sealed class TransactionItemViewModel : PropertyChangedBase
    {
        private readonly Transaction _model;
        private decimal _balance;
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
                    NotifyOfPropertyChange();
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
                    NotifyOfPropertyChange();
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
                        NotifyOfPropertyChange(nameof(Payment));
                    }
                    else
                    {
                        _model.Deposit = value;
                        if (value.HasValue)
                        {
                            _model.Payment = null;
                            NotifyOfPropertyChange(nameof(Payment));
                        }
                    }
                    NotifyOfPropertyChange();
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
                        NotifyOfPropertyChange(nameof(Deposit));
                    }
                    NotifyOfPropertyChange();
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
                    NotifyOfPropertyChange();
                }
            }
        }

        // UI-only projection state
        public decimal Balance
        {
            get => _balance;
            set
            {
                if (_balance != value)
                {
                    _balance = value;
                    NotifyOfPropertyChange();
                }
            }
        }

        public bool IsLowestNearNow
        {
            get => _isLowestNearNow;
            set
            {
                if (_isLowestNearNow != value)
                {
                    _isLowestNearNow = value;
                    NotifyOfPropertyChange();
                }
            }
        }
    }
}
