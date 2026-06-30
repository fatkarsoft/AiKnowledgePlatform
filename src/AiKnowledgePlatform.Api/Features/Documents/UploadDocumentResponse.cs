namespace AiKnowledgePlatform.Api.Features.Documents;

public sealed record UploadDocumentResponse(
    string DocumentId,
    string FileName,
    string ContentType,
    long Size,
    DocumentStatus Status);
