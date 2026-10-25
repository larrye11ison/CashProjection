using Microsoft.UI.Xaml.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class ProjectionItemEditorDialog : ContentDialog
    {
        public ProjectionItemEditorViewModel ViewModel { get; }

        public ProjectionItemEditorDialog(ProjectionItemEditorViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();

            // No Delete button at all for a brand-new (not yet saved) item.
            if (vm.IsExisting)
                SecondaryButtonText = "Delete";

            // These four fields are read/written imperatively rather than
            // via x:Bind (see the XAML's comment on NameBox for why) — set
            // their initial text here from the ViewModel's current values.
            NameBox.Text = vm.NameInput;
            DepositBox.Text = vm.DepositInput;
            PaymentBox.Text = vm.PaymentInput;
            MatchTextBox.Text = vm.Scratch.MatchText ?? string.Empty;

            // Every closing path (Save, Cancel, or a confirmed Delete) goes
            // through RequestClose — ShellViewModel is already subscribed
            // to it to tear down IsEditorOpen/EditorVM and restore grid
            // focus, so this just needs to make the dialog itself go away.
            ViewModel.RequestClose += Hide;
        }

        private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.NameInput = NameBox.Text;

        private void DepositBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.DepositInput = DepositBox.Text;

        private void PaymentBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.PaymentInput = PaymentBox.Text;

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;

            // Name/Date/Deposit/Payment are already live on the ViewModel
            // via the TextChanged handlers above (and CalendarDatePicker's
            // own immediate TwoWay push) — only MatchText still needs to be
            // read directly here, since it has no validation and so never
            // needed a dedicated tracked property.
            ViewModel.Scratch.MatchText = MatchTextBox.Text;

            ViewModel.SaveCommand.Execute(null);
        }

        private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.CancelCommand.Execute(null);
        }

        // Delete confirms in-place (ViewModel.IsConfirmingDelete swaps the
        // dialog's own content — see the XAML) rather than opening a
        // second ContentDialog on top of this one, which proved
        // unreliable across several different WinUI failure modes
        // (confirmed via the app's own crash log each time, not guessed).
        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.RequestDeleteCommand.Execute(null);
        }
    }
}
