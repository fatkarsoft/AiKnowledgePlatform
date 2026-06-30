using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed record RerankRequest(
    string Question,
    IReadOnlyList<SearchResult> Candidates,
    int TopN);
