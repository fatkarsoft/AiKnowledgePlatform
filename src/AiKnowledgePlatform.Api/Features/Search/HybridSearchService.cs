using AiKnowledgePlatform.Api.Storage;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.Features.Search;

public sealed class HybridSearchService
{
    private readonly SemanticSearchService _semanticSearchService;
    private readonly QdrantClient _qdrantClient;
    private readonly HybridSearchOptions _options;

    public HybridSearchService(
        SemanticSearchService semanticSearchService,
        QdrantClient qdrantClient,
        IOptions<HybridSearchOptions> options)
    {
        _semanticSearchService = semanticSearchService;
        _qdrantClient = qdrantClient;
        _options = options.Value;
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
        var lexicalResults = await _qdrantClient.SearchByTextAsync(question, lexicalLimit, cancellationToken);

        return MergeCandidates(semanticResults, lexicalResults)
            .OrderByDescending(candidate => candidate.Score)
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
                LexicalMatched: false);
        }

        foreach (var result in lexicalResults)
        {
            var key = (result.DocumentId, result.ChunkIndex);
            if (candidates.TryGetValue(key, out var existing))
            {
                candidates[key] = existing with { LexicalMatched = true };
                continue;
            }

            candidates[key] = new SearchCandidate(
                result.DocumentId,
                result.ChunkIndex,
                result.Text,
                result.FileName,
                SemanticScore: null,
                LexicalMatched: true);
        }

        return candidates.Values.ToArray();
    }
}
