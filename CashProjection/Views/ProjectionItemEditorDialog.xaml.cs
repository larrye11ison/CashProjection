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
            ViewModel.SaveCommand.Execute(null);
        }

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
