namespace AiKnowledgePlatform.Api.AI.Reranking;

public interface IReranker
{
    Task<IReadOnlyList<RerankResult>> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default);
}
