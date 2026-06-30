namespace AiKnowledgePlatform.Api.Features.Debug;

public sealed record DebugCandidate(
    string DocumentId,
    int ChunkIndex,
    string FileName,
    string TextPreview,
    double? SemanticScore,
    double? LexicalScore,
    double? RerankScore,
    bool FromSemantic,
    bool FromLexical);
