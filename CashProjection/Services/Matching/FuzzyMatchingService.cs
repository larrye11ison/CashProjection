using CashProjection.Data.Repositories;
using CashProjection.Models;
using FuzzySharp;

namespace CashProjection.Services.Matching
{
    // Fuzzy-matches projection items against real bank transactions.
    // Runs after every Plaid sync.
    public sealed class FuzzyMatchingService
    {
        // The user's own calibration: a projected bill and the real
        // transaction that actually paid it should land within about a
        // week of each other. A wider window risks pairing this month's
        // projection with *last* month's already-posted payment for the
        // same recurring bill.
        private const int DefaultDateWindowDays = 7;

        // Scored by MinTokenScore (see below), not a whole-string ratio —
        // the scale is different from the old Fuzz.PartialTokenSetRatio
        // default, so don't be tempted to restore 75 here.
        private const int DefaultScoreThreshold = 50;

        // See ComputeMatchScore — the bank text needs at least this many
        // tokens before its reverse-direction match is trusted.
        private const int MinReverseDirectionTokens = 2;

        private readonly ProjectionItemRepository _itemRepo;
        private readonly BankTransactionRepository _bankRepo;
        private readonly MatchRepository _matchRepo;
        private readonly AppSettingsRepository _settings;
        private readonly ChangeHistoryService _changeHistory;

        public FuzzyMatchingService(
            ProjectionItemRepository itemRepo,
            BankTransactionRepository bankRepo,
            MatchRepository matchRepo,
            AppSettingsRepository settings,
            ChangeHistoryService changeHistory)
        {
            _itemRepo = itemRepo;
            _bankRepo = bankRepo;
            _matchRepo = matchRepo;
            _settings = settings;
            _changeHistory = changeHistory;
        }

        public List<Match> FindAndRecordPresumptiveMatches(int accountId)
        {
            var windowDays = int.TryParse(_settings.Get("MatchDateWindowDays"), out var w) ? w : DefaultDateWindowDays;
            var threshold = int.TryParse(_settings.Get("MatchScoreThreshold"), out var t) ? t : DefaultScoreThreshold;

            var items = _itemRepo
                .GetByAccount(accountId)
                .Where(i => !HasConfirmedMatchForCurrentOccurrence(i))
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

                    // Matches on txn.Description (the bank's own raw text)
                    // only — txn.VendorName (Plaid's cleaned-up name) is
                    // deliberately never considered here, for now.
                    var score = ComputeMatchScore(matchText, txn.Description);
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

        // A Confirmed match only "spoken for" the occurrence it was
        // actually accepted against (Match.MatchedAtItemDate) — a recurring
        // item that reuses the same row (Monthly/Quarterly/.../
        // TwoWeeksPastLast all just bump TransactionDate in place rather
        // than insert a new row) rolls forward onto a brand new, as-yet-
        // unmatched occurrence every cycle. Without this check, the very
        // first time an item was ever confirmed-matched would permanently
        // exclude it from every future scan, since a naive "does this item
        // have any Confirmed match at all" check can never tell a stale
        // historical match from a live current one. Mirrors
        // AccountProjectionViewModel.ApplyConfirmedMatches's
        // "stillSameOccurrence" check, which is why the grid already stops
        // visually pairing a rescheduled item with its old match — this is
        // the same rule applied on the scanning side.
        private bool HasConfirmedMatchForCurrentOccurrence(ProjectionItem item) =>
            _matchRepo.GetConfirmedForProjectionItem(item.Id)?.IsForCurrentOccurrenceOf(item) == true;

        // The date window and score only apply at the moment a presumptive
        // match is created — ExistsForPair deliberately keeps the matching
        // scan from ever re-suggesting a pair that already has a row, which
        // means a pair recorded under an older/looser version of the
        // scoring or windowing logic just sits in the review queue forever,
        // never re-evaluated, even after the logic that created it has been
        // fixed. Re-check both conditions against every existing
        // presumptive match and expire whichever no longer qualifies. Call
        // this before showing the review list so it always reflects current
        // reality, not whatever logic happened to be running when each
        // match was first created.
        public int ExpireStaleMatches(int accountId)
        {
            var windowDays = int.TryParse(_settings.Get("MatchDateWindowDays"), out var w) ? w : DefaultDateWindowDays;
            var threshold = int.TryParse(_settings.Get("MatchScoreThreshold"), out var t) ? t : DefaultScoreThreshold;
            var expiredCount = 0;

            foreach (var match in _matchRepo.GetByStatus(MatchStatus.Presumptive))
            {
                var item = _itemRepo.GetById(match.ProjectionItemId);
                var txn = _bankRepo.GetById(match.BankTransactionId);
                if (item is null || txn is null || item.AccountId != accountId)
                    continue;

                var daysApart = (txn.TxnDate.Date - item.TransactionDate.Date).Duration().TotalDays;
                var matchText = string.IsNullOrWhiteSpace(item.MatchText) ? item.Name : item.MatchText;
                var currentScore = ComputeMatchScore(matchText, txn.Description);

                var reason = daysApart > windowDays
                    ? $"'{item.Name}' is now at {item.TransactionDate:d}, more than {windowDays} day(s) from the matched transaction on {txn.TxnDate:d}"
                    : currentScore < threshold
                        ? $"'{item.Name}' no longer scores as a match against '{txn.Description}' (score {currentScore}, threshold {threshold})"
                        : null;
                if (reason is null)
                    continue;

                var before = CloneMatch(match);
                match.Status = MatchStatus.Rejected;
                match.DecidedAtUtc = DateTime.UtcNow;
                _matchRepo.Update(match);
                _changeHistory.Record(
                    accountId, ChangeEntityType.Match, match.Id, ChangeType.MatchRejected,
                    before, match,
                    $"Automatically expired — {reason}");
                expiredCount++;
            }

            return expiredCount;
        }

        private static Match CloneMatch(Match m) => new Match
        {
            Id = m.Id,
            ProjectionItemId = m.ProjectionItemId,
            BankTransactionId = m.BankTransactionId,
            Status = m.Status,
            Score = m.Score,
            CreatedAtUtc = m.CreatedAtUtc,
            DecidedAtUtc = m.DecidedAtUtc,
            MatchedAtItemDate = m.MatchedAtItemDate,
        };

        // MinTokenScore alone is directional: it requires every token of
        // the FIRST argument to find a match in the second, which is right
        // when the projection text is the clean, essential side and the
        // bank text carries the noise (ACH codes, IDs, city/state). But a
        // projection item can just as easily carry its OWN extra noise —
        // e.g. a personal routing label like "P.J. MM - Digital Banking
        // Transfer to Share 0400", where everything after "P.J. MM - " is
        // the bank's real text verbatim, but "MM" has no counterpart there,
        // so the forward-only check scores it 0 and the match never gets
        // proposed despite being an obvious pairing.
        //
        // So also try it in reverse — every token of the BANK text must
        // find a match in the projection text — and take whichever
        // direction scores higher. Reverse is only trusted when the bank
        // text has at least MinReverseDirectionTokens tokens: a single
        // short/generic word (a bare "Chevron", "Valon", "AT&T" — common
        // real bank descriptions) is too easy to match by coincidence
        // against an unrelated multi-word projection name once there's no
        // second token around to corroborate it, the same reason a lone
        // coincidental token isn't trusted in the forward direction either.
        // Verified against this app's real transaction/projection data:
        // this only changes the "P.J. MM" case above — zero new false
        // positives across the full real cross-product.
        private static int ComputeMatchScore(string projectionText, string bankText)
        {
            var forward = MinTokenScore(projectionText, bankText);

            var bankTokenCount = Tokenize(bankText).Count;
            if (bankTokenCount >= MinReverseDirectionTokens)
                return Math.Max(forward, MinTokenScore(bankText, projectionText));

            // Bank text is too short/generic for fuzzy reverse-matching to
            // be trustworthy (see above). Last resort: strip purely
            // decorative punctuation ('&') instead of treating it as a word
            // boundary, and check whether the bank text's one resulting
            // word appears VERBATIM among the projection text's own words —
            // an exact match, not a fuzzy one, so it's specific enough to
            // trust without a second corroborating token. This is what lets
            // "AT&T" ($122.69, bank) match a projection item named "att
            // mobility" — "at&t" and "att" are the same word once '&' is
            // treated as punctuation rather than a separator.
            var bankExactWords = TokenizeKeepingAmpersandedWords(bankText);
            if (bankExactWords.Count == 1 && TokenizeKeepingAmpersandedWords(projectionText).Contains(bankExactWords[0]))
                return Math.Max(forward, 100);

            return forward;
        }

        private static List<string> TokenizeKeepingAmpersandedWords(string text)
        {
            char[] separators = { ' ', ':', '/', '*', '.', ',', '-', '\\' };
            return text.Replace("&", string.Empty)
                .ToLowerInvariant()
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .ToList();
        }

        // A whole-string ratio (even partial/token-set variants) can be
        // fooled by one strong shared token sitting inside an otherwise
        // unrelated, noisy bank description — e.g. "CFCU Visa" scored high
        // against an entirely different "CFCU" A2A transfer to someone
        // named Wilkins, because PartialTokenSetRatio only needs the best
        // substring alignment of the intersection, not every query token to
        // actually be represented. This scorer instead requires EVERY token
        // in the query text to have *some* reasonably similar token
        // somewhere in the target — the overall score is the weakest of
        // those per-token best-matches, so a single coincidental hit can no
        // longer carry an unrelated transaction. Used in both directions by
        // ComputeMatchScore above.
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

        // '&' matters: without it, a name like "PG&E" collapses into one
        // single 4-character blob token ("pg&e") instead of splitting into
        // its real words — and a lone short token has nothing to dilute a
        // coincidental Fuzz.Ratio hit against an unrelated noisy ACH
        // descriptor, unlike a multi-token name where a second, more
        // specific token can correctly veto a false positive.
        private static readonly char[] TokenSeparators = { ' ', ':', '/', '*', '.', ',', '-', '\\', '&' };

        private static List<string> Tokenize(string text) =>
            text.ToLowerInvariant()
                .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .ToList();
    }
}
