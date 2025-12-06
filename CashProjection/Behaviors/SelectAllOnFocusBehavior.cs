using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CashProjection.Behaviors
{
    public static class SelectAllOnFocusBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SelectAllOnFocusBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged)
            );

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e
        )
        {
            if (d is not TextBox tb)
                return;

            if (e.NewValue is true)
            {
                tb.GotKeyboardFocus += OnGotKeyboardFocus;
                tb.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            }
            else
            {
                tb.GotKeyboardFocus -= OnGotKeyboardFocus;
                tb.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            }
        }

        private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        // Ensure first mouse click focuses then triggers GotKeyboardFocus -> SelectAll
        private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                tb.Focus();
            }
        }
    }
}
