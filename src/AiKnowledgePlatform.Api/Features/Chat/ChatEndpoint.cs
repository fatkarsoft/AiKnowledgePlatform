using AiKnowledgePlatform.Api.AI;
using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.Features.Chat;

public static class ChatEndpoint
{
    private const string NotEnoughInformationAnswer = "Sağlanan bağlamda bu soruyu cevaplamak için yeterli bilgi yok.";

    public static IEndpointRouteBuilder MapChatEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat", async (
            ChatRequest request,
            HybridSearchService searchService,
            IServiceProvider services,
            IOptions<RerankingOptions> rerankingOptions,
            PromptBuilder promptBuilder,
            OllamaChatClient chatClient) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.BadRequest("Question is required.");
            }

            var finalTopN = GetFinalTopN(request, rerankingOptions.Value);
            if (finalTopN <= 0)
            {
                return Results.BadRequest("TopK must be greater than zero.");
            }

            var searchResults = await SearchAsync(request.Question, finalTopN, searchService, services, rerankingOptions.Value);
            if (searchResults.Count == 0)
            {
                return Results.Ok(new ChatResponse(NotEnoughInformationAnswer, []));
            }

            var prompt = promptBuilder.Build(request.Question, searchResults);
            var answer = await chatClient.GenerateAnswerAsync(prompt);
            var sources = MapSources(searchResults);

            return Results.Ok(new ChatResponse(answer, sources));
        })
        .WithName("Chat")
        .WithTags("Chat")
        .WithSummary("Ask a question using RAG")
        .WithDescription("Searches indexed chunks, builds a context prompt, and asks the local Ollama chat model.")
        .Produces<ChatResponse>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest);

        return app;
    }

    private static int GetFinalTopN(ChatRequest request, RerankingOptions options)
    {
        if (!options.Enabled)
        {
            return request.TopK.GetValueOrDefault(5);
        }

        return request.TopK.GetValueOrDefault(options.FinalTopN);
    }

    private static async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string question,
        int finalTopN,
        HybridSearchService searchService,
        IServiceProvider services,
        RerankingOptions options)
    {
        if (!options.Enabled)
        {
            return await searchService.SearchAsync(question, finalTopN);
        }

        var candidateCount = Math.Max(options.CandidateCount, finalTopN);
        var candidates = await searchService.SearchAsync(question, candidateCount);
        if (candidates.Count == 0)
        {
            return [];
        }

        var reranker = services.GetRequiredService<IReranker>();
        var rerankedResults = await reranker.RerankAsync(new RerankRequest(question, candidates, finalTopN));

        return rerankedResults
            .Select(result => new SearchResult(
                result.DocumentId,
                result.ChunkIndex,
                result.RerankScore,
                result.Text,
                result.FileName))
            .ToArray();
    }

    private static IReadOnlyList<ChatSource> MapSources(IReadOnlyList<SearchResult> searchResults)
    {
        return searchResults
            .Select(result =>
            {
                if (!Guid.TryParse(result.DocumentId, out var documentId))
                {
                    return null;
                }

                // In the /chat response, Score is the final context-selection score.
                // When reranking is enabled this is the reranker score; /search still
                // returns the original vector search score unchanged.
                return new ChatSource(
                    documentId,
                    result.ChunkIndex,
                    result.Score,
                    result.FileName);
            })
            .Where(source => source is not null)
            .Select(source => source!)
            .ToArray();
    }
}
