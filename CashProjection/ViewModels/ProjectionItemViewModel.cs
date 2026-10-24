using CommunityToolkit.Mvvm.ComponentModel;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // Ordered lowest to highest precedence — see ProjectionItemViewModel.Accent.
    public enum RowAccent
    {
        Normal,
        Matched,
        OverdueUnmatched,
        PendingBankActivity,
    }

    // Balance-threshold severity — drives a highlight on just the Balance
    // cell (AccountProjectionView's Balance column), not the whole row. Kept
    // separate from RowAccent/Accent below: unlike those, this is about one
    // specific cell being the thing responsible for the warning.
    public enum BalanceSeverity
    {
        Normal,
        Warning,
        Danger,
    }

    // ViewModel for a projection item (recurring bill/deposit) in the UI
    public sealed partial class ProjectionItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public int AccountId { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BalanceSeverity))]
        [NotifyPropertyChangedFor(nameof(BalanceDisplay))]
        private decimal _balance;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DepositDisplay))]
        private decimal? _deposit;

        [ObservableProperty]
        private bool _isLowestNearNow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasDistinctVendorName))]
        [NotifyPropertyChangedFor(nameof(PrimaryDisplayName))]
        [NotifyPropertyChangedFor(nameof(SecondaryDisplayText))]
        [NotifyPropertyChangedFor(nameof(HasDistinctSecondaryText))]
        private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PaymentDisplay))]
        private decimal? _payment;

        [ObservableProperty]
        private Periodicity _periodicity;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFutureDated))]
        [NotifyPropertyChangedFor(nameof(TransactionDateDisplay))]
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
        [NotifyPropertyChangedFor(nameof(Accent))]
        private Guid? _matchGroupId;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(MatchTooltip))]
        private int? _matchScore;

        [ObservableProperty]
        private bool _hasAmountDrift;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Accent))]
        private bool _isOverdueUnmatched;

        // Unmatched and dated on/before the latest real bank transaction —
        // it should have posted by now but hasn't. Distinct from
        // IsOverdueUnmatched (which fires purely off "today minus N days",
        // regardless of bank activity): this fires the moment a scheduled
        // item falls behind what the bank has actually reported, even if
        // that's within the normal overdue-warning threshold. Drives both
        // the row highlight and sorting these above the real transactions
        // they're expected to be reflected in soon.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Accent))]
        private bool _isPendingBankActivity;

        // The date used for sort order. Equal to TransactionDate, except for
        // a matched projection item, which borrows its matched real
        // transaction's date so the pair sorts adjacently.
        [ObservableProperty]
        private DateTime _effectiveSortDate = DateTime.Today;

        public bool IsMatched => MatchGroupId.HasValue;

        // WinUI styles have no DataTrigger — WPF's WarningRowStyle used a
        // Style.Triggers list where later triggers override earlier ones
        // for the same visual property when more than one matches at once.
        // This collapses that same cascade into one precedence-ordered
        // value (lowest to highest precedence, matching the original
        // trigger order) for AccountProjectionView's DataGrid.LoadingRow
        // handler to turn into an actual row Background/Foreground.
        public RowAccent Accent
        {
            get
            {
                if (IsPendingBankActivity)
                    return RowAccent.PendingBankActivity;
                if (IsOverdueUnmatched)
                    return RowAccent.OverdueUnmatched;
                if (IsMatched)
                    return RowAccent.Matched;
                return RowAccent.Normal;
            }
        }

        // Fluent 2's "warning colors," applied to just the Balance cell —
        // see BalanceSeverity's own comment for why this isn't folded into
        // Accent/RowAccent above.
        public BalanceSeverity BalanceSeverity
        {
            get
            {
                if (Balance <= Models.Thresholds.Danger)
                    return BalanceSeverity.Danger;
                if (Balance < Models.Thresholds.Warning)
                    return BalanceSeverity.Warning;
                return BalanceSeverity.Normal;
            }
        }

        // Mirrors the old FutureDateConverter threshold exactly — rows
        // dated more than a month out render at reduced opacity.
        public bool IsFutureDated => TransactionDate > DateTime.Today.AddMonths(1);

        // Pre-formatted display strings for the grid's DataGridTextColumns
        // — WinUI's Binding markup (unlike WPF's) has no StringFormat, so
        // formatting lives here instead of in XAML.
        public string TransactionDateDisplay => TransactionDate.ToString("d");
        public string DepositDisplay => Deposit?.ToString("C2") ?? string.Empty;
        public string PaymentDisplay => Payment?.ToString("C2") ?? string.Empty;
        public string BalanceDisplay => Balance.ToString("C2");

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

        // True only for the single synthetic "pending/unexplained bank
        // activity" placeholder row (see CreatePendingGapPlaceholder below)
        // — never persisted to BankTransactions, purely a display-time
        // reconciliation of a gap between the live-fetched account balance
        // and what the known, synced real transactions account for.
        // Excluded from anything that assumes a genuine Plaid transaction
        // exists: Find (ShellViewModel.UpdateSearchResults), and the
        // "latest real transaction date" used to pin overdue projections
        // above real activity (AccountProjectionViewModel.ResortAndRecalculate).
        public bool IsSyntheticPendingGap { get; private init; }

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

        // Aliases so the shared DualLineNameTemplate (App.xaml) can bind
        // uniformly across both this type and BankTransaction, which
        // expose the same shape under different names (Name vs
        // Description) — that template is the one place controlling how
        // every list in the app (the main grid, other-account transactions,
        // Match Review, Find) shows the primary/secondary name pair, so it
        // can't hard-code either type's own property names.
        public string SecondaryDisplayText => Name;
        public bool HasDistinctSecondaryText => HasDistinctVendorName;

        // True when Balance was set directly from Plaid's own running_balance
        // field (the bank's reported balance after this transaction) — an
        // authoritative fact that ResortAndRecalculate must never overwrite.
        public bool HasKnownBalance { get; private init; }

        public string BalanceSourceDescription => IsSyntheticPendingGap
            ? "Pending — already reflected in your bank's live balance, but not yet posted as a transaction"
            : IsReadOnly
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

        // Synthesizes the single placeholder row representing the gap
        // between the live account balance (Account.StartingBalance,
        // refreshed every sync) and what the known, synced real
        // transactions account for — detected live, every
        // ResortAndRecalculate pass, as the discrepancy between today's
        // balance and the most recent trusted checkpoint (Plaid-reported
        // or manually corrected) found walking backward. Can arise either
        // from ordinary Plaid sync latency (the live balance moves before
        // the transaction that caused it has synced in) or from a manual
        // correction that no longer reconciles with the live balance.
        // Never persisted to BankTransactions; recreated/updated fresh
        // every pass (see AccountProjectionViewModel.SyncPendingGapPlaceholder).
        // Returns null when there's nothing to show.
        public static ProjectionItemViewModel? CreatePendingGapPlaceholder(Account account, decimal amount)
        {
            if (Math.Abs(amount) < 0.01m)
                return null;

            var vm = new ProjectionItemViewModel
            {
                IsReadOnly = true,
                IsSyntheticPendingGap = true,
                // Deliberately true — not because Plaid reported a
                // per-transaction balance (this isn't a real transaction at
                // all), but to exempt this row from
                // ResortAndRecalculate's backward-estimate walk, which must
                // never try to "explain" it the way it does genuine
                // unknown-balance real rows. Balance (set below, and kept
                // current by SyncPendingGapPlaceholder) is the ONLY thing
                // ever allowed to assign this row's Balance, since the
                // normal walk is structurally exempted from touching it.
                HasKnownBalance = true,
                TransactionDate = DateTime.Now,
                EffectiveSortDate = DateTime.Now,
                Periodicity = Periodicity.NotApplicable,
                Name = "Pending transaction(s)",
            };
            if (amount > 0)
                vm.Deposit = amount;
            else
                vm.Payment = -amount;
            vm.Balance = account.StartingBalance;
            return vm;
        }

        // Convert to model for saving. Uses the tracked UpdatedAtUtc as-is
        // rather than stamping DateTime.UtcNow here — this is called both
        // to build comparison snapshots (before/after a possible edit) and
        // to build the model actually persisted, and stamping "now" on
        // every call made every snapshot differ from the last regardless of
        // whether anything was actually edited. Callers that represent a
        // real mutation (CommitEdit's detected-change branch, PushForward,
        // match acceptance) are responsible for bumping UpdatedAtUtc
        // themselves before calling this.
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
