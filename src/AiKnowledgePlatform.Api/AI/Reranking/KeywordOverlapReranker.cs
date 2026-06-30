using System.Text.RegularExpressions;

namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed partial class KeywordOverlapReranker : IReranker
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "be",
        "by",
        "do",
        "does",
        "for",
        "how",
        "in",
        "is",
        "it",
        "of",
        "on",
        "or",
        "that",
        "the",
        "this",
        "to",
        "what",
        "with",
        "why",
        "ve",
        "veya",
        "bir",
        "bu",
        "şu",
        "su",
        "için",
        "icin",
        "ile",
        "mi",
        "mı",
        "mu",
        "mü",
        "da",
        "de",
        "ne",
        "nedir",
        "nasıl",
        "nasil",
        "olarak"
    };

    public Task<IReadOnlyList<RerankResult>> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TopN <= 0 || request.Candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RerankResult>>([]);
        }

        var questionTokens = Tokenize(request.Question);

        // This first pass is intentionally simple and deterministic. It gives us an
        // extension point for a future cross-encoder while making the current choice
        // explainable: chunks sharing more meaningful question terms move up.
        var results = request.Candidates
            .Select(candidate =>
            {
                var candidateTokens = Tokenize(candidate.Text);
                var overlapCount = questionTokens.Count(candidateTokens.Contains);
                var rerankScore = overlapCount + candidate.Score;

                return new RerankResult(
                    candidate.DocumentId,
                    candidate.ChunkIndex,
                    candidate.Score,
                    rerankScore,
                    candidate.Text,
                    candidate.FileName);
            })
            .OrderByDescending(result => result.RerankScore)
            .ThenByDescending(result => result.OriginalScore)
            .ThenBy(result => result.ChunkIndex)
            .Take(request.TopN)
            .ToArray();

        return Task.FromResult<IReadOnlyList<RerankResult>>(results);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return Tokenizer()
            .Split(text.ToLowerInvariant())
            .Where(token => token.Length > 1)
            .Where(token => !Stopwords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Tokenizer();
}
