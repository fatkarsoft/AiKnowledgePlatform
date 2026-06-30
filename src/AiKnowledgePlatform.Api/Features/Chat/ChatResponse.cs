namespace AiKnowledgePlatform.Api.Features.Chat;

public sealed record ChatResponse(
    string Answer,
    IReadOnlyList<ChatSource> Sources);

public sealed record ChatSource(
    Guid DocumentId,
    int ChunkIndex,
    double Score,
    string FileName);
