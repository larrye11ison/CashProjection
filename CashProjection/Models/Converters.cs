using System.Globalization;
using System.Windows.Data;

namespace CashProjection.Models
{
    public class BalanceToDangerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal balance)
            {
                return balance <= Thresholds.Danger;
            }
            return false;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    public class BalanceToWarningConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal balance)
            {
                return balance < Thresholds.Warning && balance > Thresholds.Danger;
            }
            return false;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    public class FutureDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime date)
            {
                return date > DateTime.Today.AddMonths(1);
            }
            return false;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }
    }

    // Real bank-transaction rows carry Periodicity.NotApplicable since the
    // concept doesn't apply to them — show blank there instead of the raw
    // enum name.
    public class PeriodicityToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Periodicity.NotApplicable)
                return string.Empty;
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Keeps the account-switcher ComboBox a steady width instead of
    // auto-sizing to whichever account's name/balance happens to be
    // selected — scales with the window (50% of its width) but never
    // grows past ~10cm, converted to WPF's device-independent units
    // (96 px/inch over 2.54 cm/inch).
    public class WindowWidthToAccountComboWidthConverter : IValueConverter
    {
        private const double MaxWidthDip = 10.0 * 96.0 / 2.54;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double windowWidth)
                return Math.Min(windowWidth * 0.5, MaxWidthDip);
            return MaxWidthDip;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    internal static class Thresholds
    {
        public const decimal Danger = 500m;
        public const decimal Warning = 2000m;
    }
}
