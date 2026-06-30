namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed class RerankingOptions
{
    public const string SectionName = "Reranking";

    public bool Enabled { get; set; } = true;

    public int CandidateCount { get; set; } = 20;

    public int FinalTopN { get; set; } = 5;
}
