using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Api.AI.Reranking;

public sealed class HttpReranker : IReranker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly RerankerServiceOptions _options;

    public HttpReranker(HttpClient httpClient, IOptions<RerankerServiceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
    }

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        RerankRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.TopN <= 0 || request.Candidates.Count == 0)
        {
            return [];
        }

        var payload = new TeiRerankRequest(
            request.Question,
            request.Candidates.Select(candidate => candidate.Text).ToArray());

        using var response = await _httpClient.PostAsJsonAsync(
            _options.Endpoint.TrimStart('/'),
            payload,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var teiResults = await response.Content.ReadFromJsonAsync<TeiRerankResponse[]>(
            JsonOptions,
            cancellationToken);

        return (teiResults ?? [])
            .Where(result => result.Index >= 0 && result.Index < request.Candidates.Count)
            .Select(result =>
            {
                var candidate = request.Candidates[result.Index];

                return new RerankResult(
                    candidate.DocumentId,
                    candidate.ChunkIndex,
                    candidate.Score,
                    result.Score,
                    candidate.Text,
                    candidate.FileName);
            })
            .OrderByDescending(result => result.RerankScore)
            .Take(request.TopN)
            .ToArray();
    }

    private sealed record TeiRerankRequest(string Query, IReadOnlyList<string> Texts);

    private sealed record TeiRerankResponse(int Index, double Score);
}
