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

        private bool _dialogHostInitialized;

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
            // dialogs, e.g. Delete confirmation) needs a live XamlRoot,
            // which isn't available until the window is actually activated.
            Activated += (_, _) =>
            {
                if (_dialogHostInitialized)
                    return;
                _dialogHostInitialized = true;
                DialogHost.Root = Content.XamlRoot;
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
