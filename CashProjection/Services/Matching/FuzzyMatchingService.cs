using CashProjection.Data.Repositories;
using CashProjection.Models;
using FuzzySharp;

namespace CashProjection.Services.Matching
{
    // Fuzzy-matches projection items against real bank transactions.
    // Runs after every Plaid sync.
    public sealed class FuzzyMatchingService
    {
        private const int DefaultDateWindowDays = 5;

        // Scored by MinTokenScore (see below), not a whole-string ratio —
        // the scale is different from the old Fuzz.PartialTokenSetRatio
        // default, so don't be tempted to restore 75 here.
        private const int DefaultScoreThreshold = 50;

        private readonly ProjectionItemRepository _itemRepo;
        private readonly BankTransactionRepository _bankRepo;
        private readonly MatchRepository _matchRepo;
        private readonly AppSettingsRepository _settings;

        public FuzzyMatchingService(
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            MatchRepository matchRepo,
            AppSettingsRepository settings)
        {
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _matchRepo = matchRepo;
            _settings = settings;
        }

        public List<Match> FindAndRecordPresumptiveMatches(int accountId)
        {
            var windowDays = int.TryParse(_settings.Get("MatchDateWindowDays"), out var w) ? w : DefaultDateWindowDays;
            var threshold = int.TryParse(_settings.Get("MatchScoreThreshold"), out var t) ? t : DefaultScoreThreshold;

            var items = _itemRepo
                .GetByAccount(accountId)
                .Where(i => _matchRepo.GetConfirmedForProjectionItem(i.Id) is null)
                .ToList();
            var transactions = _bankRepo.GetByAccount(accountId);

            var created = new List<Match>();
            foreach (var item in items)
            {
                var isPayment = item.Payment.HasValue;
                var matchText = string.IsNullOrWhiteSpace(item.MatchText) ? item.Name : item.MatchText;

                foreach (var txn in transactions)
                {
                    if ((txn.TxnDate.Date - item.TransactionDate.Date).Duration().TotalDays > windowDays)
                        continue;

                    // Plaid's amount convention: positive = money out (a payment), negative = money in (a deposit).
                    var txnIsPayment = txn.Amount > 0;
                    if (txnIsPayment != isPayment)
                        continue;

                    if (_matchRepo.ExistsForPair(item.Id, txn.Id))
                        continue;

                    // A transaction that's already Confirmed against some
                    // other item is spoken for — it can never be suggested
                    // again for a different projection item, no matter how
                    // well it happens to score against it.
                    if (_matchRepo.GetConfirmedForBankTransaction(txn.Id) is not null)
                        continue;

                    var score = MinTokenScore(matchText, txn.Description);
                    if (score < threshold)
                        continue;

                    var match = new Match
                    {
                        ProjectionItemId = item.Id,
                        BankTransactionId = txn.Id,
                        Status = MatchStatus.Presumptive,
                        Score = score,
                        CreatedAtUtc = DateTime.UtcNow,
                    };
                    _matchRepo.Insert(match);
                    created.Add(match);
                }
            }

            return created;
        }

        // A whole-string ratio (even partial/token-set variants) can be
        // fooled by one strong shared token sitting inside an otherwise
        // unrelated, noisy bank description — e.g. "CFCU Visa" scored high
        // against an entirely different "CFCU" A2A transfer to someone
        // named Wilkins, because PartialTokenSetRatio only needs the best
        // substring alignment of the intersection, not every query token to
        // actually be represented. This scorer instead requires EVERY token
        // in the projection item's match text to have *some* reasonably
        // similar token somewhere in the bank description — the overall
        // score is the weakest of those per-token best-matches, so a single
        // coincidental hit can no longer carry an unrelated transaction.
        private static int MinTokenScore(string queryText, string targetText)
        {
            var queryTokens = Tokenize(queryText);
            var targetTokens = Tokenize(targetText);
            if (queryTokens.Count == 0 || targetTokens.Count == 0)
                return 0;

            var minScore = int.MaxValue;
            foreach (var queryToken in queryTokens)
            {
                var best = 0;
                foreach (var targetToken in targetTokens)
                {
                    var score = Fuzz.Ratio(queryToken, targetToken);
                    if (score > best)
                        best = score;
                }
                if (best < minScore)
                    minScore = best;
            }
            return minScore;
        }

        private static readonly char[] TokenSeparators = { ' ', ':', '/', '*', '.', ',', '-', '\\' };

        private static List<string> Tokenize(string text) =>
            text.ToLowerInvariant()
                .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .ToList();
    }
}
