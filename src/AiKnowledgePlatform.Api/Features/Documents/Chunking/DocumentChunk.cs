namespace AiKnowledgePlatform.Api.Features.Documents.Chunking;

public sealed record DocumentChunk(
    Guid DocumentId,
    int Index,
    string Text,
    int CharacterCount,
    DateTime CreatedAt);
