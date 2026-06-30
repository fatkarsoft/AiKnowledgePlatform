namespace AiKnowledgePlatform.Api.Features.Search;

public sealed record SearchRequest(string? Query, int? TopK);
