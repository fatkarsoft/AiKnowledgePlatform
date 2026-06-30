using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Storage;

namespace AiKnowledgePlatform.Api.Features.Search;

public sealed class SemanticSearchService
{
    private readonly OllamaEmbeddingGenerator _embeddingGenerator;
    private readonly QdrantClient _qdrantClient;

    public SemanticSearchService(
        OllamaEmbeddingGenerator embeddingGenerator,
        QdrantClient qdrantClient)
    {
        _embeddingGenerator = embeddingGenerator;
        _qdrantClient = qdrantClient;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var vector = await _embeddingGenerator.GenerateEmbeddingAsync(query, cancellationToken);

        return await _qdrantClient.SearchAsync(vector, topK, cancellationToken);
    }
}
