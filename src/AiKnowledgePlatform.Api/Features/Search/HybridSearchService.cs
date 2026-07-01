using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.Features.Search;

public sealed class HybridSearchService
{
    private readonly SemanticSearchService _semanticSearchService;
    private readonly LexicalSearchService _lexicalSearchService;
    private readonly HybridSearchOptions _options;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(
        SemanticSearchService semanticSearchService,
        LexicalSearchService lexicalSearchService,
        IOptions<HybridSearchOptions> options,
        ILogger<HybridSearchService> logger)
    {
        _semanticSearchService = semanticSearchService;
        _lexicalSearchService = lexicalSearchService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string question,
        int candidateCount,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await _semanticSearchService.SearchAsync(question, candidateCount, cancellationToken);
        }

        var semanticLimit = Math.Max(candidateCount, _options.SemanticCandidateCount);
        var lexicalLimit = Math.Max(1, _options.LexicalCandidateCount);
        var finalLimit = Math.Max(1, Math.Min(candidateCount, _options.FinalCandidateCount));

        var semanticResults = await _semanticSearchService.SearchAsync(question, semanticLimit, cancellationToken);
        var lexicalResults = await _lexicalSearchService.SearchAsync(question, lexicalLimit, cancellationToken);
        var mergedCandidates = MergeCandidates(semanticResults, lexicalResults)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();

        _logger.LogInformation(
            "Hybrid search merged candidate count: {CandidateCount}",
            mergedCandidates.Length);

        return mergedCandidates
            .Take(finalLimit)
            .Select(candidate => candidate.ToSearchResult())
            .ToArray();
    }

    public static IReadOnlyList<SearchCandidate> MergeCandidates(
        IReadOnlyList<SearchResult> semanticResults,
        IReadOnlyList<SearchResult> lexicalResults)
    {
        var candidates = new Dictionary<(string DocumentId, int ChunkIndex), SearchCandidate>();

        foreach (var result in semanticResults)
        {
            var key = (result.DocumentId, result.ChunkIndex);
            candidates[key] = new SearchCandidate(
                result.DocumentId,
                result.ChunkIndex,
                result.Text,
                result.FileName,
                result.Score,
                LexicalScore: null,
                LexicalMatched: false);
        }

        foreach (var result in lexicalResults)
        {
            var key = (result.DocumentId, result.ChunkIndex);
            if (candidates.TryGetValue(key, out var existing))
            {
                candidates[key] = existing with
                {
                    LexicalScore = result.Score,
                    LexicalMatched = true
                };
                continue;
            }

            candidates[key] = new SearchCandidate(
                result.DocumentId,
                result.ChunkIndex,
                result.Text,
                result.FileName,
                SemanticScore: null,
                result.Score,
                LexicalMatched: true);
        }

        return candidates.Values.ToArray();
    }
}
