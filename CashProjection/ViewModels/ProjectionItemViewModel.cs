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
        [NotifyPropertyChangedFor(nameof(HasDistinctVendorName))]
        [NotifyPropertyChangedFor(nameof(PrimaryDisplayName))]
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

        // Bank-row-only (null for editable projection items): Plaid's
        // cleaned-up merchant name, kept separate from Name (which holds
        // the bank's own raw description text for a bank row). Set once at
        // construction and never mutated afterward, since bank rows aren't
        // editable.
        public string? VendorName { get; private init; }

        // True only when this is a bank row AND its vendor name actually
        // differs from the raw description — the grid cell shows both
        // lines only in that case, collapsing to a single line otherwise
        // (including for every ordinary editable projection item, where
        // VendorName is always null).
        public bool HasDistinctVendorName =>
            !string.IsNullOrWhiteSpace(VendorName)
            && !string.Equals(VendorName.Trim(), Name.Trim(), StringComparison.Ordinal);

        // The grid's primary (top-line) text for this row — the vendor name
        // when there is one, otherwise just Name.
        public string PrimaryDisplayName => string.IsNullOrWhiteSpace(VendorName) ? Name : VendorName;

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
                VendorName = txn.VendorName,
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

        // Convert to model for saving. Uses the tracked UpdatedAtUtc as-is
        // rather than stamping DateTime.UtcNow here — this is called both
        // to build comparison snapshots (before/after a possible edit) and
        // to build the model actually persisted, and stamping "now" on
        // every call made every snapshot differ from the last regardless of
        // whether anything was actually edited. Callers that represent a
        // real mutation (Save's detected-change branch, PushForward, match
        // acceptance) are responsible for bumping UpdatedAtUtc themselves
        // before calling this.
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
            UpdatedAtUtc = UpdatedAtUtc,
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
