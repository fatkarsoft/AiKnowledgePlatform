namespace AiKnowledgePlatform.Api.Features.Chat;

public sealed record ChatRequest(string? Question, int? TopK);
