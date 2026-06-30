namespace AiKnowledgePlatform.Api.Features.Debug;

public sealed record RetrievalDebugResponse(
    string Question,
    IReadOnlyList<DebugCandidate> SemanticCandidates,
    IReadOnlyList<DebugCandidate> LexicalCandidates,
    IReadOnlyList<DebugCandidate> MergedCandidates,
    IReadOnlyList<DebugCandidate> RerankedCandidates,
    IReadOnlyList<DebugCandidate> PromptContext,
    DebugTimings Timings);
