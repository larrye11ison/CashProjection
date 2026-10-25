using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace CashProjection.Models
{
    // Validates a raw amount string typed into the Deposit/Payment fields
    // of the projection item editor. Blank is valid — both fields are
    // optional, and blank just means "not set" — but anything actually
    // entered must parse as a non-negative decimal with no sub-cent
    // fraction (real money doesn't have fractions of a cent).
    public sealed class CurrencyAmountAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return true;

            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
                return false;

            if (amount < 0)
                return false;

            return amount == decimal.Round(amount, 2);
        }
    }
}
