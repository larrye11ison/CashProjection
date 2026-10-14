using System.Globalization;
using System.IO;
using System.Text;

namespace CashProjection.Services.DevTools
{
    public sealed record CsvTestRow(DateTime Date, string Description, decimal Amount);

    // DEV-ONLY. Reads the exact Chevron Federal Credit Union export column
    // layout (Transaction Number,Date,Description,Memo,Amount Debit,Amount
    // Credit,Balance,check_number,Fees / MM/dd/yyyy dates) so the fuzzy
    // matching engine's threshold/date-window can be tuned against real
    // bank text before Plaid data is flowing. This is NOT a general bank-CSV
    // importer — Plaid's real shape (merchant_name/name, signed amount)
    // differs — and rows from here are never persisted into BankTransactions.
    public static class ChevronCsvTestImporter
    {
        public static List<CsvTestRow> Load(string path)
        {
            var rows = new List<CsvTestRow>();
            var lines = File.ReadAllLines(path);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = SplitCsvLine(line);
                if (fields.Count < 6)
                    continue;

                if (!DateTime.TryParseExact(fields[1], "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                var memo = fields[3].Trim();
                var description = string.IsNullOrWhiteSpace(memo) ? fields[2].Trim() : memo;

                // Mirror Plaid's sign convention: positive = payment (money out), negative = deposit (money in).
                decimal amount;
                if (decimal.TryParse(fields[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var debit) && debit != 0)
                    amount = debit;
                else if (decimal.TryParse(fields[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var credit) && credit != 0)
                    amount = -credit;
                else
                    continue;

                rows.Add(new CsvTestRow(date, description, amount));
            }

            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                    inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());
            return fields;
        }
    }
}
