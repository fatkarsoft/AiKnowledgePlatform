using AiKnowledgePlatform.Api.Storage;

namespace AiKnowledgePlatform.Api.Features.Search;

public sealed class LexicalSearchService
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
        "çalışır",
        "calisir",
        "olarak"
    };

    private readonly QdrantClient _qdrantClient;
    private readonly ILogger<LexicalSearchService> _logger;

    public LexicalSearchService(
        QdrantClient qdrantClient,
        ILogger<LexicalSearchService> logger)
    {
        _qdrantClient = qdrantClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string question,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var terms = ExtractSearchTerms(question);

        _logger.LogInformation("Hybrid lexical question: {Question}", question);
        _logger.LogInformation("Hybrid lexical extracted terms: {Terms}", string.Join(", ", terms));

        if (limit <= 0 || terms.Count == 0)
        {
            _logger.LogInformation("Hybrid lexical returned candidate count: 0");

            return [];
        }

        var resultsByChunk = new Dictionary<(string DocumentId, int ChunkIndex), SearchResult>();

        foreach (var term in terms)
        {
            _logger.LogInformation("Hybrid lexical SearchByTextAsync term: {Term}", term);

            var termResults = await _qdrantClient.SearchByTextAsync(term, limit, cancellationToken);

            _logger.LogInformation(
                "Hybrid lexical SearchByTextAsync returned {CandidateCount} candidates for term {Term}",
                termResults.Count,
                term);

            foreach (var result in termResults)
            {
                var key = (result.DocumentId, result.ChunkIndex);
                if (resultsByChunk.TryGetValue(key, out var existing))
                {
                    resultsByChunk[key] = existing with { Score = existing.Score + 1 };
                    continue;
                }

                resultsByChunk[key] = result with { Score = 1 };
            }
        }

        var results = resultsByChunk.Values
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToArray();

        _logger.LogInformation("Hybrid lexical returned candidate count: {CandidateCount}", results.Length);

        return results;
    }

    public static IReadOnlyList<string> ExtractSearchTerms(string question)
    {
        var separators = question
            .Where(character => !char.IsLetterOrDigit(character))
            .Distinct()
            .ToArray();

        return question
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 1)
            .Where(term => !Stopwords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
