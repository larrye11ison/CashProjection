using System.Globalization;
using Caliburn.Micro;

namespace CashProjection.Models
{
    public enum Periodicity
    {
        NotApplicable,
        Monthly,
        Quarterly,
        SemiAnnually,
        Annually,
        TwoWeeksPastLast,
    }

    // Pure domain model (POCO) – no UI dependencies, no INPC, no parsing.
    public sealed class Transaction
    {
        public string Name { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; } = DateTime.Now.Date;
        public decimal? Deposit { get; set; }
        public decimal? Payment { get; set; }
        public Periodicity Periodicity { get; set; }
    }
}
