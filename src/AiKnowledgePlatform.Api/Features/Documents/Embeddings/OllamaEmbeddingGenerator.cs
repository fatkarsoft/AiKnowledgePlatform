using System.Net.Http.Json;

namespace AiKnowledgePlatform.Api.Features.Documents.Embeddings;

public class OllamaEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _embeddingModel;

    public OllamaEmbeddingGenerator(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _embeddingModel = configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public virtual async Task<float[]> GenerateEmbeddingAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaEmbedRequest(_embeddingModel, input);
        using var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var embedResponse = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(
            cancellationToken: cancellationToken);

        return embedResponse?.Embeddings.FirstOrDefault()
            ?? throw new InvalidOperationException("Ollama did not return an embedding.");
    }

    public string Model => _embeddingModel;

    private sealed record OllamaEmbedRequest(string Model, string Input);

    private sealed record OllamaEmbedResponse(float[][] Embeddings);
}
