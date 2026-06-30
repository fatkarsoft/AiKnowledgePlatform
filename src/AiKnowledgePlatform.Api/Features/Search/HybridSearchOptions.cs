namespace AiKnowledgePlatform.Api.Features.Search;

public sealed class HybridSearchOptions
{
    public const string SectionName = "HybridSearch";

    public bool Enabled { get; set; } = true;

    public int SemanticCandidateCount { get; set; } = 20;

    public int LexicalCandidateCount { get; set; } = 20;

    public int FinalCandidateCount { get; set; } = 20;
}
