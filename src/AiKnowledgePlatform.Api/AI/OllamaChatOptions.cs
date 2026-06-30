namespace AiKnowledgePlatform.Api.AI;

public sealed class OllamaChatOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string ChatModel { get; set; } = "llama3.1:8b";
}
