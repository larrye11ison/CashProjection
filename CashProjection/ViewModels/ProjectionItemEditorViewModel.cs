using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CashProjection.Models;

namespace CashProjection.ViewModels
{
    // Backs the projection item editor popover — the single place
    // projection items are now added, edited, or deleted (replacing
    // in-grid cell editing entirely). Edits happen on a scratch copy of
    // the item so Cancel is a true no-op; Save copies the scratch fields
    // back onto the live item (or, for a brand-new item, inserts it for
    // the first time).
    //
    // Derives from ObservableValidator (part of CommunityToolkit.Mvvm,
    // already a project dependency) rather than pulling in a separate
    // validation package — System.ComponentModel.DataAnnotations
    // attributes on the [ObservableProperty] fields below give GetErrors()/
    // HasErrors real meaning once ValidateProperty is called (see
    // RevalidateAndNotify — the generated property setter only copies the
    // attributes for metadata, it does not validate automatically).
    public sealed partial class ProjectionItemEditorViewModel : ObservableValidator
    {
        private readonly AccountProjectionViewModel _accountVM;
        private readonly ProjectionItemViewModel? _target;

        public event Action? RequestClose;

        public ProjectionItemViewModel Scratch { get; }

        public bool IsNew => _target is null;
        public bool IsExisting => _target is not null;
        public string Title => IsNew ? "Add projection item" : "Edit projection item";

        // All enum values for the Periodicity ComboBox — WinUI has no
        // ObjectDataProvider/x:Type-enum-reflection markup like WPF, so this
        // is exposed from the ViewModel instead.
        public Periodicity[] PeriodicityOptions { get; } = Enum.GetValues<Periodicity>();

        [ObservableProperty]
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
        private string _nameInput;

        [ObservableProperty]
        [Required(ErrorMessage = "Date is required.")]
        private DateTimeOffset? _dateInput;

        [ObservableProperty]
        [CurrencyAmount(ErrorMessage = "Deposit must be a non-negative amount with at most 2 decimal places.")]
        private string _depositInput;

        [ObservableProperty]
        [CurrencyAmount(ErrorMessage = "Payment must be a non-negative amount with at most 2 decimal places.")]
        private string _paymentInput;

        public string? NameError => GetErrors(nameof(NameInput)).FirstOrDefault()?.ErrorMessage;
        public string? DateError => GetErrors(nameof(DateInput)).FirstOrDefault()?.ErrorMessage;
        public string? DepositError => GetErrors(nameof(DepositInput)).FirstOrDefault()?.ErrorMessage;
        public string? PaymentError => GetErrors(nameof(PaymentInput)).FirstOrDefault()?.ErrorMessage;

        // Cross-field — exactly one of Deposit/Payment must be entered
        // (zero counts as entered; neither, or both, does not) — so it
        // can't be expressed as a DataAnnotations attribute on either
        // property alone the way the others above are. Tracked manually
        // instead of via GetErrors.
        private string? _amountError;
        public string? AmountError => _amountError;

        public bool CanSave => !HasErrors && AmountError is null;

        // The dialog's own Save button also factors in IsConfirmingDelete,
        // so it can't be clicked out from under the delete-confirm panel.
        public bool CanSaveNow => CanSave && !IsConfirmingDelete;

        // [ObservableProperty]'s generated setter only copies the
        // validation attributes onto the generated property for metadata
        // purposes — it does NOT call ValidateProperty automatically on
        // every set (confirmed by inspecting the generated code directly
        // rather than assuming). Each hook below re-validates its own
        // property explicitly so HasErrors/GetErrors actually stay current
        // as the user types, instead of only reflecting whatever
        // ValidateAllProperties() saw once at construction time.
        partial void OnNameInputChanged(string value) => RevalidateAndNotify(value, nameof(NameInput), nameof(NameError));
        partial void OnDateInputChanged(DateTimeOffset? value) => RevalidateAndNotify(value, nameof(DateInput), nameof(DateError));

        partial void OnDepositInputChanged(string value)
        {
            RevalidateAndNotify(value, nameof(DepositInput), nameof(DepositError));
            RevalidateAmounts();
        }

        partial void OnPaymentInputChanged(string value)
        {
            RevalidateAndNotify(value, nameof(PaymentInput), nameof(PaymentError));
            RevalidateAmounts();
        }

        private void RevalidateAndNotify(object? value, string propertyName, string errorPropertyName)
        {
            ValidateProperty(value, propertyName);
            OnPropertyChanged(errorPropertyName);
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanSaveNow));
        }

        private void RevalidateAmounts()
        {
            var depositSet = !string.IsNullOrWhiteSpace(DepositInput);
            var paymentSet = !string.IsNullOrWhiteSpace(PaymentInput);

            var error = (depositSet, paymentSet) switch
            {
                (false, false) => "Enter either a Deposit or a Payment amount.",
                (true, true) => "Enter a Deposit or a Payment amount, not both.",
                _ => null,
            };

            if (error == _amountError)
                return;

            _amountError = error;
            OnPropertyChanged(nameof(AmountError));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanSaveNow));
        }

        public ProjectionItemEditorViewModel(AccountProjectionViewModel accountVM, ProjectionItemViewModel? target)
        {
            _accountVM = accountVM;
            _target = target;
            Scratch = target is null
                ? accountVM.BuildNewItemScratch()
                : new ProjectionItemViewModel(target.ToModel());

            _nameInput = Scratch.Name;
            // TransactionDate is a plain calendar date with no timezone
            // meaning — CalendarDatePicker.Date needs a DateTimeOffset
            // though, and always DISPLAYS it converted to the local time
            // zone. Labeling it Local (not Utc) here means the offset
            // baked into the DateTimeOffset is the correct local one, so
            // converting back to local for display is a no-op and the
            // date shown matches the date stored. Labeling it Utc instead
            // (the previous bug) meant local midnight got reinterpreted as
            // UTC midnight, which then displayed as the PREVIOUS day for
            // any time zone west of UTC once converted back to local.
            _dateInput = new DateTimeOffset(DateTime.SpecifyKind(Scratch.TransactionDate, DateTimeKind.Local));
            _depositInput = Scratch.Deposit?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            _paymentInput = Scratch.Payment?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

            // Seeds error/CanSave state immediately — e.g. a brand-new item
            // starts with a blank Name, so Save must already be disabled
            // before the user has touched anything, not just after.
            ValidateAllProperties();
            RevalidateAmounts();
        }

        [RelayCommand]
        public void Save()
        {
            // Defense in depth — the dialog's Save button is already
            // disabled while CanSave is false, but Enter-key handling in
            // WinUI has surprised us once already on this exact dialog.
            if (!CanSave || DateInput is not { } date)
                return;

            Scratch.Name = NameInput.Trim();
            // .Date (not .UtcDateTime.Date) — see the constructor's
            // comment. Converting to UTC here would have the same bug in
            // the opposite direction, shifting the saved date FORWARD a
            // day for any time zone east of UTC.
            Scratch.TransactionDate = date.Date;

            var deposit = ParseAmount(DepositInput);
            var payment = ParseAmount(PaymentInput);
            if (deposit.HasValue)
            {
                Scratch.Deposit = deposit;
            }
            else
            {
                Scratch.Payment = payment;
                if (!payment.HasValue)
                    Scratch.Deposit = null;
            }

            if (IsNew)
                _accountVM.CommitNewItem(Scratch);
            else
                _accountVM.CommitEdit(_target!, Scratch);

            RequestClose?.Invoke();
        }

        private static decimal? ParseAmount(string text) =>
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var d) ? d : null;

        [RelayCommand]
        public void Cancel() => RequestClose?.Invoke();

        // Delete confirms IN-PLACE within this same popover — swapping in
        // a small "are you sure" panel (see ProjectionItemEditorDialog.xaml)
        // in place of the normal form fields — rather than showing a
        // second ContentDialog. A nested ContentDialog was the original
        // design, but proved unreliable in practice across several
        // different WinUI failure modes (XamlRoot not resolving, a
        // button-click deferral already completed by the time it was
        // released), each confirmed via the app's own crash log rather
        // than guessed. WinUI only ever reliably supports one ContentDialog
        // open at a time; this sidesteps that limitation entirely instead
        // of continuing to fight it.
        [ObservableProperty]
        private bool _isConfirmingDelete;

        public bool IsEditingFields => !IsConfirmingDelete;

        partial void OnIsConfirmingDeleteChanged(bool value)
        {
            OnPropertyChanged(nameof(IsEditingFields));
            OnPropertyChanged(nameof(CanSaveNow));
        }

        public string DeleteConfirmationMessage => _target is null
            ? string.Empty
            : $"Delete '{_target.Name}' — {DeleteAmountText} on {_target.TransactionDate:d}? This isn't permanent — it can be undone from Change History.";

        private string DeleteAmountText => _target switch
        {
            { Deposit: { } d } => $"deposit of {d:C}",
            { Payment: { } p } => $"payment of {p:C}",
            _ => "amount of $0",
        };

        [RelayCommand(CanExecute = nameof(IsExisting))]
        public void RequestDelete() => IsConfirmingDelete = true;

        [RelayCommand]
        public void CancelDelete() => IsConfirmingDelete = false;

        [RelayCommand]
        public void ConfirmDelete()
        {
            if (_target is null)
                return;

            _accountVM.DeleteTransactionConfirmed(_target);
            RequestClose?.Invoke();
        }
    }
}
