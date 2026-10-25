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

            // Every closing path (Save, Cancel, or a confirmed Delete) goes
            // through RequestClose — ShellViewModel is already subscribed
            // to it to tear down IsEditorOpen/EditorVM and restore grid
            // focus, so this just needs to make the dialog itself go away.
            ViewModel.RequestClose += Hide;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;

            // x:Bind TwoWay on a TextBox's Text only pushes to the
            // ViewModel on LostFocus — a mouse click on this button already
            // blurs whatever TextBox had focus before PrimaryButtonClick
            // fires, but DefaultButton="Primary" invoking this same click
            // via Enter does NOT (and shuffling focus to a non-Control
            // panel to force it turned out unreliable), so Save would
            // otherwise read stale Scratch data for whichever field the
            // user was still editing. Reading every TextBox directly here
            // sidesteps the LostFocus dependency entirely.
            ViewModel.Scratch.Name = NameBox.Text;
            ViewModel.Scratch.Deposit = ParseNullableDecimal(DepositBox.Text);
            ViewModel.Scratch.Payment = ParseNullableDecimal(PaymentBox.Text);
            ViewModel.Scratch.MatchText = MatchTextBox.Text;

            ViewModel.SaveCommand.Execute(null);
        }

        private static decimal? ParseNullableDecimal(string text) =>
            decimal.TryParse(text, out var d) ? d : null;

        private void ContentDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.CancelCommand.Execute(null);
        }

        // Deferred: Delete asks for confirmation (IDialogService, itself
        // async) before actually removing anything — the dialog must stay
        // open until that's resolved, and only close if the user actually
        // confirmed (ViewModel.Delete only raises RequestClose then).
        private async void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                await ViewModel.DeleteCommand.ExecuteAsync(null);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
