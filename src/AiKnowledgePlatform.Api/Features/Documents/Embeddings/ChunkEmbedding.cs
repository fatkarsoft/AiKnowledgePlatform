namespace AiKnowledgePlatform.Api.Features.Documents.Embeddings;

public sealed record ChunkEmbedding(
    Guid DocumentId,
    int ChunkIndex,
    string Model,
    int Dimension,
    float[] Vector,
    DateTime CreatedAt);
