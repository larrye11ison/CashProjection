using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.WinUI.UI.Controls;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    public sealed partial class AccountProjectionView : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel), typeof(AccountProjectionViewModel), typeof(AccountProjectionView),
            new PropertyMetadata(null, OnViewModelChanged));

        // Per-row PropertyChanged subscriptions, keyed by the bound item so
        // UnloadingRow can detach the exact same delegate LoadingRow
        // attached — the grid recycles row containers, so this can't just
        // assume one row maps to one item for the control's lifetime.
        private readonly Dictionary<ProjectionItemViewModel, PropertyChangedEventHandler> _rowHandlers = new();

        public AccountProjectionView()
        {
            InitializeComponent();
            Loaded += (_, _) => ViewModel?.SetViewReference(this);
        }

        public AccountProjectionViewModel? ViewModel
        {
            get => (AccountProjectionViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AccountProjectionView view && e.NewValue is AccountProjectionViewModel vm)
                view.ViewModel?.SetViewReference(view);
        }

        // WinUI styles have no DataTrigger, so the row-level highlighting
        // WPF's WarningRowStyle did declaratively is applied here instead,
        // driven by ProjectionItemViewModel.Accent/IsFutureDated/
        // IsSyntheticPendingGap (see RowAccentBrushes and task notes there).
        private void TransactionsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is not ProjectionItemViewModel vm)
                return;

            ApplyRowVisuals(e.Row, vm);

            void Handler(object? s, PropertyChangedEventArgs args)
            {
                if (args.PropertyName is nameof(ProjectionItemViewModel.Accent)
                    or nameof(ProjectionItemViewModel.IsFutureDated))
                {
                    ApplyRowVisuals(e.Row, vm);
                }
            }

            vm.PropertyChanged += Handler;
            _rowHandlers[vm] = Handler;
        }

        private void TransactionsGrid_UnloadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is ProjectionItemViewModel vm && _rowHandlers.TryGetValue(vm, out var handler))
            {
                vm.PropertyChanged -= handler;
                _rowHandlers.Remove(vm);
            }
        }

        private static void ApplyRowVisuals(DataGridRow row, ProjectionItemViewModel vm)
        {
            var (background, foreground) = RowAccentBrushes.Resolve(vm.Accent);
            row.Background = background;
            if (foreground is not null)
                row.Foreground = foreground;
            row.FontStyle = vm.IsSyntheticPendingGap ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            row.Opacity = vm.IsFutureDated ? 0.5 : 1.0;
        }
    }
}
