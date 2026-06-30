using System.Diagnostics;
using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Search;
using AiKnowledgePlatform.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.Features.Debug;

public static class RetrievalDebugEndpoint
{
    private const int TextPreviewMaxLength = 300;

    public static IEndpointRouteBuilder MapRetrievalDebugEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/debug/retrieval", async (
            RetrievalDebugRequest request,
            SemanticSearchService semanticSearchService,
            QdrantClient qdrantClient,
            IServiceProvider services,
            IOptions<HybridSearchOptions> hybridSearchOptions,
            IOptions<RerankingOptions> rerankingOptions,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest("Question is required.");
            }

            var question = request.Question;
            var hybridOptions = hybridSearchOptions.Value;
            var rerankOptions = rerankingOptions.Value;
            var candidateCount = request.CandidateCount.GetValueOrDefault(
                hybridOptions.SemanticCandidateCount > 0 ? hybridOptions.SemanticCandidateCount : 20);
            var finalTopN = request.FinalTopN.GetValueOrDefault(
                rerankOptions.FinalTopN > 0 ? rerankOptions.FinalTopN : 5);

            if (candidateCount <= 0)
            {
                return Results.BadRequest("CandidateCount must be greater than zero.");
            }

            if (finalTopN <= 0)
            {
                return Results.BadRequest("FinalTopN must be greater than zero.");
            }

            var totalTimer = Stopwatch.StartNew();

            var stageTimer = Stopwatch.StartNew();
            var semanticResults = await semanticSearchService.SearchAsync(question, candidateCount, cancellationToken);
            stageTimer.Stop();
            var semanticSearchMs = stageTimer.ElapsedMilliseconds;

            IReadOnlyList<SearchResult> lexicalResults = [];
            long lexicalSearchMs = 0;
            if (hybridOptions.Enabled)
            {
                stageTimer.Restart();
                var lexicalLimit = hybridOptions.LexicalCandidateCount > 0
                    ? hybridOptions.LexicalCandidateCount
                    : candidateCount;
                lexicalResults = await qdrantClient.SearchByTextAsync(question, lexicalLimit, cancellationToken);
                stageTimer.Stop();
                lexicalSearchMs = stageTimer.ElapsedMilliseconds;
            }

            stageTimer.Restart();
            var mergedCandidates = HybridSearchService.MergeCandidates(semanticResults, lexicalResults)
                .OrderByDescending(candidate => candidate.Score)
                .Take(candidateCount)
                .ToArray();
            stageTimer.Stop();
            var mergeMs = stageTimer.ElapsedMilliseconds;

            IReadOnlyList<DebugCandidate> rerankedCandidates;
            long rerankMs = 0;
            if (rerankOptions.Enabled)
            {
                stageTimer.Restart();
                var reranker = services.GetRequiredService<IReranker>();
                var rerankResults = await reranker.RerankAsync(
                    new RerankRequest(
                        question,
                        mergedCandidates.Select(candidate => candidate.ToSearchResult()).ToArray(),
                        mergedCandidates.Length),
                    cancellationToken);
                stageTimer.Stop();
                rerankMs = stageTimer.ElapsedMilliseconds;

                var mergedByKey = mergedCandidates.ToDictionary(
                    candidate => (candidate.DocumentId, candidate.ChunkIndex));
                rerankedCandidates = rerankResults
                    .Select(result => ToDebugCandidate(result, mergedByKey))
                    .ToArray();
            }
            else
            {
                rerankedCandidates = mergedCandidates
                    .Select(candidate => ToDebugCandidate(candidate, rerankScore: null))
                    .ToArray();
            }

            var promptContext = rerankedCandidates.Take(finalTopN).ToArray();
            totalTimer.Stop();

            var response = new RetrievalDebugResponse(
                question,
                semanticResults.Select(ToSemanticDebugCandidate).ToArray(),
                lexicalResults.Select(ToLexicalDebugCandidate).ToArray(),
                mergedCandidates.Select(candidate => ToDebugCandidate(candidate, rerankScore: null)).ToArray(),
                rerankedCandidates,
                promptContext,
                new DebugTimings(
                    semanticSearchMs,
                    lexicalSearchMs,
                    mergeMs,
                    rerankMs,
                    totalTimer.ElapsedMilliseconds));

            return Results.Ok(response);
        })
        .WithName("DebugRetrieval")
        .WithTags("Debug")
        .WithSummary("Debug retrieval pipeline")
        .WithDescription("Shows semantic, lexical, merged, reranked, and final prompt context candidates without calling the chat model.")
        .Produces<RetrievalDebugResponse>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static DebugCandidate ToSemanticDebugCandidate(SearchResult result)
    {
        return new DebugCandidate(
            result.DocumentId,
            result.ChunkIndex,
            result.FileName,
            CreatePreview(result.Text),
            result.Score,
            LexicalScore: null,
            RerankScore: null,
            FromSemantic: true,
            FromLexical: false);
    }

    private static DebugCandidate ToLexicalDebugCandidate(SearchResult result)
    {
        return new DebugCandidate(
            result.DocumentId,
            result.ChunkIndex,
            result.FileName,
            CreatePreview(result.Text),
            SemanticScore: null,
            result.Score,
            RerankScore: null,
            FromSemantic: false,
            FromLexical: true);
    }

    private static DebugCandidate ToDebugCandidate(SearchCandidate candidate, double? rerankScore)
    {
        return new DebugCandidate(
            candidate.DocumentId,
            candidate.ChunkIndex,
            candidate.FileName,
            CreatePreview(candidate.Text),
            candidate.SemanticScore,
            candidate.LexicalMatched ? 1 : null,
            rerankScore,
            candidate.SemanticScore.HasValue,
            candidate.LexicalMatched);
    }

    private static DebugCandidate ToDebugCandidate(
        RerankResult result,
        IReadOnlyDictionary<(string DocumentId, int ChunkIndex), SearchCandidate> mergedByKey)
    {
        if (mergedByKey.TryGetValue((result.DocumentId, result.ChunkIndex), out var candidate))
        {
            return ToDebugCandidate(candidate, result.RerankScore);
        }

        return new DebugCandidate(
            result.DocumentId,
            result.ChunkIndex,
            result.FileName,
            CreatePreview(result.Text),
            result.OriginalScore,
            LexicalScore: null,
            result.RerankScore,
            FromSemantic: true,
            FromLexical: false);
    }

    private static string CreatePreview(string text)
    {
        if (text.Length <= TextPreviewMaxLength)
        {
            return text;
        }

        return text[..TextPreviewMaxLength];
    }
}
