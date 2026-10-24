using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using CashProjection.ViewModels;

namespace CashProjection.Views
{
    // Fluent 2 "warning colors" for AccountProjectionView's Balance column
    // — applied to just that cell (via these two converters, bound
    // straight off BalanceSeverity in the column's CellTemplate), not the
    // whole row, so it's unambiguous that the balance is what's being
    // flagged. UnsetValue (not null) on Normal, so the cell keeps whatever
    // default Background/Foreground the template itself sets.
    public sealed class BalanceSeverityToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is BalanceSeverity severity && Resolve(severity).Background is { } brush
                ? brush
                : DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();

        internal static (Brush? Background, Brush? Foreground) Resolve(BalanceSeverity severity) => severity switch
        {
            BalanceSeverity.Danger => (
                RowAccentBrushes.TryGet("SystemFillColorCriticalBrush", Color.FromArgb(255, 196, 43, 28)),
                RowAccentBrushes.TryGet("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.White)),

            BalanceSeverity.Warning => (
                RowAccentBrushes.TryGet("SystemFillColorCautionBrush", Color.FromArgb(255, 157, 93, 0)),
                new SolidColorBrush(Microsoft.UI.Colors.Black)),

            _ => (null, null),
        };
    }

    public sealed class BalanceSeverityToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is BalanceSeverity severity && BalanceSeverityToBackgroundConverter.Resolve(severity).Foreground is { } brush
                ? brush
                : DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }
}
