using System.Globalization;
using CashProjection.Models;

namespace CashProjection.Services
{
    public static class TransactionParser
    {
        /// <summary>
        /// Parses a line in the format: Name, Date, Amount, Periodicity
        /// Amount >= 0 -> Deposit; Amount < 0 -> Payment (stored as positive).
        /// </summary>
        public static Transaction Parse(string str)
        {
            if (string.IsNullOrWhiteSpace(str))
                throw new ArgumentException(
                    "Input cannot be null, empty, or whitespace.",
                    nameof(str)
                );

            var parts = str.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length != 4)
                throw new FormatException(
                    $"Expected 4 comma-separated values: 'Name, Date, Amount, Periodicity'. Got {parts.Length}."
                );

            var (name, dateText, amountText, periodText) = (parts[0], parts[1], parts[2], parts[3]);

            if (
                !DateTime.TryParse(
                    dateText,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                throw new FormatException($"Couldn't parse date: '{dateText}'.");

            if (
                !decimal.TryParse(
                    amountText,
                    NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.CurrentCulture,
                    out var amount
                )
            )
                throw new FormatException($"Couldn't parse amount: '{amountText}'.");

            if (!Enum.TryParse(periodText, ignoreCase: true, out Periodicity periodicity))
                throw new FormatException($"Couldn't parse periodicity: '{periodText}'.");

            return new Transaction
            {
                Name = name,
                TransactionDate = date,
                Periodicity = periodicity,
                Deposit = amount >= 0 ? amount : null,
                Payment = amount < 0 ? -amount : null,
            };
        }
    }
}
