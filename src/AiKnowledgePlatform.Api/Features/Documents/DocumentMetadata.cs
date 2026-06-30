namespace AiKnowledgePlatform.Api.Features.Documents;

public sealed record DocumentMetadata(
    Guid DocumentId,
    string FileName,
    string ContentType,
    long Size,
    DocumentStatus Status,
    DateTime UploadedAt);
