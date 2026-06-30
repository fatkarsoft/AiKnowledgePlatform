namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed record RerankResult(
    string DocumentId,
    int ChunkIndex,
    double OriginalScore,
    double RerankScore,
    string Text,
    string FileName);
