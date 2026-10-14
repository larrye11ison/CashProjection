using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // ViewModel for a projection item (recurring bill/deposit) in the UI
    public sealed partial class ProjectionItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int AccountId { get; set; }

        [ObservableProperty]
        private decimal _balance;

        [ObservableProperty]
        private decimal? _deposit;

        [ObservableProperty]
        private bool _isLowestNearNow;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private decimal? _payment;

        [ObservableProperty]
        private Periodicity _periodicity;

        [ObservableProperty]
        private DateTime _transactionDate = DateTime.Today;

        [ObservableProperty]
        private string? _matchText;

        [ObservableProperty]
        private bool _needsReview;

        // Shared by a projection item and the real BankTransaction it's
        // been matched to, so the grid can sort them adjacent to each other
        // and tint them to show they're linked.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsMatched))]
        private Guid? _matchGroupId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MatchTooltip))]
        private int? _matchScore;

        [ObservableProperty]
        private bool _hasAmountDrift;

        [ObservableProperty]
        private bool _isOverdueUnmatched;

        // The date used for sort order. Equal to TransactionDate, except for
        // a matched projection item, which borrows its matched real
        // transaction's date so the pair sorts adjacently.
        [ObservableProperty]
        private DateTime _effectiveSortDate = DateTime.Today;

        public bool IsMatched => MatchGroupId.HasValue;

        public string? MatchTooltip => MatchScore.HasValue
            ? $"Linked to a real transaction (match score {MatchScore})"
            : null;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        // True for rows that mirror a real BankTransaction rather than a
        // user-editable projection item — the grid shows both side by side,
        // but only projection items can be edited, pushed forward, or deleted.
        public bool IsReadOnly { get; private init; }
        public bool IsEditable => !IsReadOnly;
        public int? BankTransactionId { get; private init; }

        // True when Balance was set directly from Plaid's own running_balance
        // field (the bank's reported balance after this transaction) — an
        // authoritative fact that ResortAndRecalculate must never overwrite.
        public bool HasKnownBalance { get; private init; }

        public string BalanceSourceDescription => IsReadOnly
            ? (HasKnownBalance ? "Reported by your bank" : "Estimated — your bank didn't report a balance for this transaction")
            : "Projected estimate";

        // Default constructor for new projection items
        public ProjectionItemViewModel()
        {
        }

        // Constructor to load from a model (when loading from the database)
        public ProjectionItemViewModel(ProjectionItem model)
        {
            Id = model.Id;
            AccountId = model.AccountId;
            Name = model.Name;
            TransactionDate = model.TransactionDate;
            EffectiveSortDate = model.TransactionDate;
            Deposit = model.Deposit;
            Payment = model.Payment;
            Periodicity = model.Periodicity;
            MatchText = model.MatchText;
            NeedsReview = model.NeedsReview;
            UpdatedAtUtc = model.UpdatedAtUtc;
        }

        // Read-only display row for a real bank transaction — never
        // persisted as a ProjectionItems row.
        public static ProjectionItemViewModel FromBankTransaction(BankTransaction txn)
        {
            var vm = new ProjectionItemViewModel
            {
                IsReadOnly = true,
                BankTransactionId = txn.Id,
                TransactionDate = txn.TxnDate,
                EffectiveSortDate = txn.TxnDate,
                Periodicity = Periodicity.NotApplicable,
                HasKnownBalance = txn.RunningBalance.HasValue,
            };
            // Plaid convention: positive amount = payment (money out), negative = deposit (money in).
            if (txn.Amount > 0)
                vm.Payment = txn.Amount;
            else if (txn.Amount < 0)
                vm.Deposit = -txn.Amount;
            vm.Name = txn.Description;
            if (txn.RunningBalance.HasValue)
                vm.Balance = txn.RunningBalance.Value;
            return vm;
        }

        // Convert to model for saving
        public ProjectionItem ToModel() => new ProjectionItem
        {
            Id = Id,
            AccountId = AccountId,
            Name = Name,
            TransactionDate = TransactionDate,
            Deposit = Deposit,
            Payment = Payment,
            Periodicity = Periodicity,
            MatchText = MatchText,
            NeedsReview = NeedsReview,
            UpdatedAtUtc = DateTime.UtcNow,
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
