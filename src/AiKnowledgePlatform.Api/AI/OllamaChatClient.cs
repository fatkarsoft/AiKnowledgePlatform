using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.AI;

public class OllamaChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _chatModel;

    public OllamaChatClient(HttpClient httpClient, IOptions<OllamaChatOptions> options)
    {
        _httpClient = httpClient;
        _chatModel = options.Value.ChatModel;
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl);
    }

    public virtual async Task<string> GenerateAnswerAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest(
            _chatModel,
            [new OllamaChatMessage("user", prompt)],
            false);

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var chatResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);

        return chatResponse?.Message?.Content
            ?? throw new InvalidOperationException("Ollama did not return a chat response.");
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream);

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaChatResponse(OllamaChatMessage? Message);
}
