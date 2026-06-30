namespace AiKnowledgePlatform.Api.Features.Debug;

public sealed record DebugTimings(
    long SemanticSearchMs,
    long LexicalSearchMs,
    long MergeMs,
    long RerankMs,
    long TotalMs);
