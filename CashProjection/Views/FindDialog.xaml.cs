using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class FindDialog : ContentDialog
    {
        public ShellViewModel ViewModel { get; }

        public FindDialog(ShellViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();

            // Opened (not Loaded) — Loaded can fire while the dialog is
            // still being constructed/animated in, before it's actually
            // taken over keyboard focus from whatever had it before
            // (which is exactly how this ended up silently focus-stealing
            // back to the grid behind it).
            Opened += (_, _) =>
            {
                FindTextBox.Focus(FocusState.Programmatic);
                FindTextBox.SelectAll();
            };

            CloseButtonClick += (_, _) => ViewModel.CancelFind();
        }

        // x:Bind TwoWay on TextBox.Text wasn't reliably pushing keystrokes
        // into FindText live — driving it explicitly off TextChanged
        // guarantees every keystroke updates the search immediately
        // (Text itself stays a one-way display binding, so there's no
        // feedback loop).
        private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
            ViewModel.FindText = FindTextBox.Text;

        // Cancel the dialog's own default button-closes-dialog behavior and
        // decide for ourselves — ConfirmFind() is a no-op (and shouldn't
        // close anything) when there's no result to jump to.
        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true;
            ViewModel.ConfirmFind();
            if (!ViewModel.IsFindOpen)
                Hide();
        }

        // Routing Escape/Enter/Up/Down through the TextBox's own KeyDown
        // marks the event Handled, which stops it from ever reaching
        // ContentDialog's native Escape-to-dismiss handling — so this has
        // to own closing the dialog itself rather than relying on that.
        private void FindTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            ViewModel.OnFindKeyDown(e);
            if (!ViewModel.IsFindOpen)
                Hide();
        }

        private void ResultsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ViewModel.ConfirmFind();
            if (!ViewModel.IsFindOpen)
                Hide();
        }
    }
}
