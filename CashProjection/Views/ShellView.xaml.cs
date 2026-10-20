using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public partial class ShellView : Window
    {
        public ShellView(ShellViewModel vm)
        {
            InitializeComponent();

            DataContext = vm;
            vm.SetViewReference(this);
            // Note: AccountVM.SetViewReference is set by AccountProjectionView itself
        }

        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.OnFindKeyDown(e);
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.ConfirmFind();
            }
        }

        // Shared Escape-to-dismiss handling for the History/Matches/Plaid
        // Settings/editor overlays. Deliberately NOT relying on IsCancel on
        // each popover's own Close button — every one of those buttons
        // lives in the same Window's visual tree simultaneously (only
        // Visibility differs), and WPF only ever honors a single "cancel
        // button" per Window, so with several IsCancel=true buttons
        // present at once, Escape would reliably hit just one of them
        // (whichever last claimed that slot) regardless of which popover
        // is actually showing. Explicit handling here, on each popover's
        // own overlay Grid, sidesteps that entirely.
        private void Overlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || DataContext is not ShellViewModel vm)
                return;

            e.Handled = true;
            vm.CancelActiveOverlay();
        }

        // The editor overlay gets its own handler, deliberately on the
        // bubbling KeyDown event rather than the tunneling PreviewKeyDown
        // the other overlays use above. The editor has a ComboBox
        // (Periodicity) and a DatePicker, both of which have their own
        // legitimate use for Escape/Enter (closing their own open dropdown/
        // calendar popup) — those controls mark the key handled themselves
        // during their own processing, which happens before this bubbles
        // up here, so this only fires once nothing closer to the focused
        // element already consumed the key. Also deliberately NOT using
        // IsDefault/IsCancel on the Save/Cancel buttons: every popover's
        // buttons are simultaneously loaded in this same Window (only
        // Visibility differs), and WPF only resolves one such button per
        // Window when several are present — undefined which one Enter/
        // Escape actually reaches. Explicit handling here removes that
        // ambiguity.
        private void EditorOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ShellViewModel { EditorVM: { } editorVM })
                return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                editorVM.SaveCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                editorVM.CancelCommand.Execute(null);
            }
        }

        // The overlay Grids and the main DataGrid are SIBLINGS in the
        // visual tree, not ancestor/descendant — routed key events (the
        // PreviewKeyDown/KeyDown handlers above) only ever travel along
        // the focused element's own ancestor chain. Toggling a popover's
        // Visibility to Visible does nothing to keyboard focus by itself,
        // so without this, focus silently stays wherever it already was
        // (typically the grid), and Escape/Enter on the popover never
        // reaches those handlers at all. IsVisibleChanged fires every time
        // a popover actually opens (unlike Loaded, which only fires once
        // for the lifetime of these permanently-instantiated UserControls),
        // so this reliably pulls focus in on every open.
        private void Overlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not UIElement { IsVisible: true } root)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => FocusFirstFocusable(root)));
        }

        // The editor specifically prefers its Name TextBox over the header's
        // Close icon button (which would otherwise win as "first focusable
        // element" — it's declared before the real fields) so opening the
        // editor is immediately ready for typing, same as Find's own text box.
        private void EditorOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not UIElement { IsVisible: true } root)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!FocusFirstOfType<TextBox>(root))
                    FocusFirstFocusable(root);
            }));
        }

        private static bool FocusFirstFocusable(DependencyObject parent)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is UIElement { Focusable: true, IsVisible: true, IsEnabled: true } ui && ui.Focus())
                    return true;
                if (FocusFirstFocusable(child))
                    return true;
            }
            return false;
        }

        private static bool FocusFirstOfType<T>(DependencyObject parent) where T : UIElement
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T { IsVisible: true, IsEnabled: true } target && target.Focus())
                    return true;
                if (FocusFirstOfType<T>(child))
                    return true;
            }
            return false;
        }
    }
}
