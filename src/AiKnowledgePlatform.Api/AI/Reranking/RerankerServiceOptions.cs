namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed class RerankerServiceOptions
{
    public const string SectionName = "RerankerService";

    public string BaseUrl { get; set; } = "http://localhost:8081";

    public string Endpoint { get; set; } = "/rerank";

    public int TimeoutSeconds { get; set; } = 30;
}
