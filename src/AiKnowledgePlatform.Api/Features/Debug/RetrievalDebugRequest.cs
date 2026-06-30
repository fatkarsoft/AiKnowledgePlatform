namespace AiKnowledgePlatform.Api.Features.Debug;

public sealed record RetrievalDebugRequest(
    string? Question,
    int? CandidateCount,
    int? FinalTopN);
