using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CashProjection.Services.Dialogs;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class ShellView : Window
    {
        private const double MaxAccountComboWidthDip = 10.0 * 96.0 / 2.54;

        public ShellViewModel ViewModel { get; }

        public ShellView(ShellViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();

            Title = "Cash Projection";
            WindowBackdropHelper.ApplyMica(this);
            WindowIconHelper.SetIcon(this, "Assets\\AppIcon.ico");

            ProjectionView.ViewModel = ViewModel.AccountVM;

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            RootGrid.SizeChanged += (_, e) =>
                AccountCombo.MaxWidth = Math.Min(e.NewSize.Width * 0.5, MaxAccountComboWidthDip);

            // DialogHost.Root (used by IDialogService's confirm/info
            // dialogs, e.g. the editor popover's Delete confirmation) needs
            // a live XamlRoot. A one-time "set it on the first Activated"
            // guard here used to leave it permanently stuck at null:
            // XamlRoot isn't necessarily available yet on that very first
            // activation (observed in practice — ConfirmAsync later threw
            // "This element does not have a XamlRoot" when Delete was
            // clicked, from cp_crash.txt), and the guard prevented ever
            // retrying once that first, too-early attempt had already run.
            // Updating on every activation instead is self-healing: each
            // later activation gets another chance to capture a valid
            // XamlRoot if the first one couldn't. Guarded defensively —
            // accessing Content has also been observed (same crash log)
            // throwing "The WinUI Desktop Window object has already been
            // closed" during some activation-timing edge case; there's
            // nothing useful to do here if that happens, so just skip it.
            Activated += (_, _) =>
            {
                try
                {
                    if (Content.XamlRoot is { } root)
                        DialogHost.Root = root;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                }
            };
        }

        // Each of the six former hand-rolled overlays is now a
        // ContentDialog, shown here the moment ShellViewModel flips the
        // corresponding IsXxxOpen flag — the flag itself (and tearing it
        // back down) is still owned by ShellViewModel exactly as before;
        // this is purely "and also show/hide the actual dialog."
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShellViewModel.AccountVM):
                    ProjectionView.ViewModel = ViewModel.AccountVM;
                    break;

                case nameof(ShellViewModel.IsFindOpen) when ViewModel.IsFindOpen:
                    DeferShow(() => new FindDialog(ViewModel));
                    break;

                case nameof(ShellViewModel.IsChangeHistoryOpen) when ViewModel.IsChangeHistoryOpen:
                    DeferShow(() => new ChangeHistoryDialog(ViewModel.ChangeHistoryVM!));
                    break;

                case nameof(ShellViewModel.IsMatchReviewOpen) when ViewModel.IsMatchReviewOpen:
                    DeferShow(() => new MatchReviewDialog(ViewModel.MatchReviewVM!));
                    break;

                case nameof(ShellViewModel.IsPlaidSettingsOpen) when ViewModel.IsPlaidSettingsOpen:
                    DeferShow(() => new PlaidEnvironmentSettingsDialog(ViewModel.PlaidSettingsVM!));
                    break;

                case nameof(ShellViewModel.IsEditorOpen) when ViewModel.IsEditorOpen:
                    DeferShow(() => new ProjectionItemEditorDialog(ViewModel.EditorVM!));
                    break;

                case nameof(ShellViewModel.IsBalanceCorrectionOpen) when ViewModel.IsBalanceCorrectionOpen:
                    DeferShow(() => new BalanceCorrectionDialog(ViewModel.BalanceCorrectionVM!));
                    break;
            }
        }

        // We're still on the call stack of whatever UI event (a toolbar
        // button's Click) set the IsXxxOpen flag that triggered this —
        // calling ShowAsync() reentrantly, before that event has finished
        // being processed, is what made every dialog appear without ever
        // actually taking keyboard focus (it silently stayed on whatever
        // had it before — the grid behind the dialog). Deferring to a
        // fresh dispatcher cycle lets the originating event finish first.
        private void DeferShow(Func<ContentDialog> factory)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = factory();
                dialog.XamlRoot = Content.XamlRoot;
                await dialog.ShowAsync();
            });
        }
    }
}
