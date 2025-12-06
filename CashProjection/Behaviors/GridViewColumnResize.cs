using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CashProjection.Behaviors
{
    // Usage:
    // - Enable on ListView: behaviors:GridViewColumnResize.IsEnabled="True"
    // - Per column set: behaviors:GridViewColumnResize.Width="Auto" or "*"
    //   (numeric widths like "120" are also supported)
    public static class GridViewColumnResize
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(GridViewColumnResize),
                new PropertyMetadata(false, OnIsEnabledChanged)
            );

        public static readonly DependencyProperty WidthProperty =
            DependencyProperty.RegisterAttached(
                "Width",
                typeof(string),
                typeof(GridViewColumnResize),
                new PropertyMetadata(null, OnColumnWidthChanged)
            );

        private static readonly List<WeakReference<ListView>> s_enabledListViews = new();

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        public static string? GetWidth(DependencyObject element) =>
            (string?)element.GetValue(WidthProperty);

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static void SetWidth(DependencyObject element, string? value) =>
            element.SetValue(WidthProperty, value);

        private static T? FindDescendant<T>(DependencyObject? d)
            where T : DependencyObject
        {
            if (d is null)
                return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is T match)
                    return match;
                var result = FindDescendant<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static bool IsAuto(string spec) =>
            string.Equals(spec, "Auto", StringComparison.OrdinalIgnoreCase);

        private static bool IsStar(string spec) =>
            spec == "*" || spec.EndsWith("*", StringComparison.Ordinal);

        private static void OnColumnWidthChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            // d is a GridViewColumn (not a Visual). We cannot walk its parent via VisualTreeHelper.
            // Instead, trigger recalculation on all live, enabled ListViews that are currently loaded.
            PruneDeadListViews();

            foreach (var wr in s_enabledListViews)
            {
                if (wr.TryGetTarget(out var lv) && lv.IsLoaded && GetIsEnabled(lv))
                {
                    Recalculate(lv);
                }
            }
        }

        private static void OnIsEnabledChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (d is not ListView listView)
                return;

            if ((bool)e.NewValue)
            {
                s_enabledListViews.Add(new WeakReference<ListView>(listView));
                listView.Loaded += OnListViewLoaded;
                listView.SizeChanged += OnListViewSizeChanged;
            }
            else
            {
                listView.Loaded -= OnListViewLoaded;
                listView.SizeChanged -= OnListViewSizeChanged;

                // prune this instance if present
                s_enabledListViews.RemoveAll(wr =>
                    !wr.TryGetTarget(out var lv) || ReferenceEquals(lv, listView)
                );
            }
        }

        private static void OnListViewLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListView listView)
            {
                // First layout pass, then compute widths
                listView.Dispatcher.InvokeAsync(() => Recalculate(listView));
            }
        }

        private static void OnListViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ListView listView)
                Recalculate(listView);
        }

        private static void PruneDeadListViews()
        {
            s_enabledListViews.RemoveAll(wr => !wr.TryGetTarget(out _));
        }

        private static void Recalculate(ListView listView)
        {
            if (listView.View is not GridView gridView)
                return;

            // Ensure layout is up-to-date before measuring content widths
            listView.UpdateLayout();

            double fixedAndAutoWidth = 0.0;
            GridViewColumn? starColumn = null;

            foreach (var col in gridView.Columns)
            {
                var spec = GetWidth(col);
                if (string.IsNullOrWhiteSpace(spec))
                {
                    // Leave as-is (treated as fixed width)
                    fixedAndAutoWidth += col.ActualWidth;
                    continue;
                }

                spec = spec.Trim();
                if (IsStar(spec))
                {
                    starColumn = col;
                    continue;
                }

                if (IsAuto(spec))
                {
                    // Force auto-measure, then capture the realized width
                    col.Width = double.NaN;
                    // After setting NaN, we need a layout pass to realize ActualWidth
                    listView.UpdateLayout();
                    fixedAndAutoWidth += col.ActualWidth;
                    continue;
                }

                if (
                    double.TryParse(
                        spec,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out var fixedWidth
                    )
                    && fixedWidth > 0
                )
                {
                    col.Width = fixedWidth;
                    fixedAndAutoWidth += fixedWidth;
                    continue;
                }

                // Fallback: treat as auto
                col.Width = double.NaN;
                listView.UpdateLayout();
                fixedAndAutoWidth += col.ActualWidth;
            }

            if (starColumn != null)
            {
                double listWidth = listView.ActualWidth;

                // Account for grid padding/border and possible vertical scrollbar
                double chrome = 6; // small padding fudge
                var scrollViewer = FindDescendant<ScrollViewer>(listView);
                if (
                    scrollViewer != null
                    && scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                )
                {
                    chrome += SystemParameters.VerticalScrollBarWidth;
                }

                double available = Math.Max(0, listWidth - fixedAndAutoWidth - chrome);

                // Minimum sensible width to keep header readable
                starColumn.Width = Math.Max(60, available);
            }
        }
    }
}
