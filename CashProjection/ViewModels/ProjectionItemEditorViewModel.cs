using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CashProjection.ViewModels
{
    // Backs the projection item editor popover — the single place
    // projection items are now added, edited, or deleted (replacing
    // in-grid cell editing entirely). Edits happen on a scratch copy of
    // the item so Cancel is a true no-op; Save copies the scratch fields
    // back onto the live item (or, for a brand-new item, inserts it for
    // the first time).
    public sealed partial class ProjectionItemEditorViewModel : ObservableObject
    {
        private readonly AccountProjectionViewModel _accountVM;
        private readonly ProjectionItemViewModel? _target;

        public event Action? RequestClose;

        public ProjectionItemViewModel Scratch { get; }

        public bool IsNew => _target is null;
        public bool IsExisting => _target is not null;
        public string Title => IsNew ? "Add projection item" : "Edit projection item";

        public ProjectionItemEditorViewModel(AccountProjectionViewModel accountVM, ProjectionItemViewModel? target)
        {
            _accountVM = accountVM;
            _target = target;
            Scratch = target is null
                ? accountVM.BuildNewItemScratch()
                : new ProjectionItemViewModel(target.ToModel());
        }

        [RelayCommand]
        public void Save()
        {
            if (IsNew)
                _accountVM.CommitNewItem(Scratch);
            else
                _accountVM.CommitEdit(_target!, Scratch);

            RequestClose?.Invoke();
        }

        [RelayCommand]
        public void Cancel() => RequestClose?.Invoke();

        // Reuses AccountProjectionViewModel.DeleteTransaction's own
        // confirm-and-delete flow directly (not via its ICommand wrapper)
        // so this can tell whether the user actually confirmed — closing
        // the popover only when the item was really removed, not when
        // they backed out of the confirmation.
        [RelayCommand(CanExecute = nameof(IsExisting))]
        public void Delete()
        {
            if (_target is null)
                return;

            _accountVM.DeleteTransaction(_target);
            if (!_accountVM.Transactions.Contains(_target))
                RequestClose?.Invoke();
        }
    }
}
