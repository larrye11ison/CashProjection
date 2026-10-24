using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace CashProjection.Models
{
    // WinUI, unlike WPF, ships no built-in BooleanToVisibilityConverter.
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is Visibility v && v == Visibility.Visible;
    }

    // Real bank-transaction rows carry Periodicity.NotApplicable since the
    // concept doesn't apply to them — show blank there instead of the raw
    // enum name.
    public class PeriodicityToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Periodicity.NotApplicable)
                return string.Empty;
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    // WinUI's Binding has no automatic string<->decimal? conversion for
    // TextBox.Text the way WPF's did — this is the editor popover's bridge
    // for the Deposit/Payment fields.
    public class NullableDecimalToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is decimal d ? d.ToString() : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            value is string s && decimal.TryParse(s, out var d) ? d : null!;
    }

    // Thresholds for ProjectionItemViewModel.Accent (the balance-driven row
    // highlighting) — kept here rather than inlined there since these are
    // tuning knobs, not presentation logic.
    internal static class Thresholds
    {
        public const decimal Danger = 500m;
        public const decimal Warning = 2000m;
    }
}
