namespace AiKnowledgePlatform.Api.Features.Search;

public sealed record SearchResult(
    string DocumentId,
    int ChunkIndex,
    double Score,
    string Text,
    string FileName);
