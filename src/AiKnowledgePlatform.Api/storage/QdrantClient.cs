using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiKnowledgePlatform.Api.Features.Documents;
using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Api.Storage;

public class QdrantClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly int _vectorSize;

    public QdrantClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _collectionName = configuration["Qdrant:CollectionName"] ?? "document_chunks";
        _vectorSize = int.TryParse(configuration["Qdrant:VectorSize"], out var vectorSize) ? vectorSize : 768;

        var baseUrl = configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    public virtual async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
    {
        var request = new CreateCollectionRequest(new VectorConfig(_vectorSize, "Cosine"));
        using var response = await _httpClient.PutAsJsonAsync(
            $"/collections/{_collectionName}",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            response.EnsureSuccessStatusCode();
        }

        await EnsureTextPayloadIndexAsync(cancellationToken);
    }

    public virtual async Task UpsertChunksAsync(
        DocumentMetadata metadata,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<ChunkEmbedding> embeddings,
        CancellationToken cancellationToken = default)
    {
        var embeddingsByChunk = embeddings.ToDictionary(embedding => embedding.ChunkIndex);
        var points = new List<QdrantPoint>();

        foreach (var chunk in chunks.OrderBy(chunk => chunk.Index))
        {
            if (!embeddingsByChunk.TryGetValue(chunk.Index, out var embedding))
            {
                throw new InvalidOperationException($"Missing embedding for chunk {chunk.Index}.");
            }

            points.Add(new QdrantPoint(
                CreatePointId(chunk.DocumentId, chunk.Index),
                embedding.Vector,
                new QdrantPayload(
                    chunk.DocumentId.ToString(),
                    chunk.Index,
                    chunk.Text,
                    metadata.FileName,
                    metadata.ContentType,
                    chunk.CreatedAt)));
        }

        var request = new UpsertPointsRequest(points);
        using var response = await _httpClient.PutAsJsonAsync(
            $"/collections/{_collectionName}/points?wait=true",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public virtual async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] vector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchPointsRequest(vector, topK, true);
        using var response = await _httpClient.PostAsJsonAsync(
            $"/collections/{_collectionName}/points/search",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var searchResponse = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(
            JsonOptions,
            cancellationToken);

        return searchResponse?.Result
            .Where(point => point.Payload is not null)
            .Select(point => new SearchResult(
                point.Payload!.DocumentId,
                point.Payload.ChunkIndex,
                point.Score,
                point.Payload.Text,
                point.Payload.FileName))
            .ToArray() ?? [];
    }

    public virtual async Task<IReadOnlyList<SearchResult>> SearchByTextAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var terms = ExtractTextSearchTerms(query);
        if (limit <= 0 || terms.Count == 0)
        {
            return [];
        }

        var resultsByChunk = new Dictionary<(string DocumentId, int ChunkIndex), SearchResult>();

        foreach (var term in terms)
        {
            var termResults = await SearchByTextTermAsync(term, limit, cancellationToken);
            foreach (var result in termResults)
            {
                var key = (result.DocumentId, result.ChunkIndex);
                if (resultsByChunk.TryGetValue(key, out var existing))
                {
                    resultsByChunk[key] = existing with { Score = existing.Score + 1 };
                    continue;
                }

                resultsByChunk[key] = result with { Score = 1 };
            }
        }

        return resultsByChunk.Values
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToArray();
    }

    private async Task<IReadOnlyList<SearchResult>> SearchByTextTermAsync(
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        var request = new ScrollPointsRequest(
            new QdrantFilter(
            [
                new TextMatchCondition("text", new TextMatch(term))
            ]),
            limit,
            true);

        using var response = await _httpClient.PostAsJsonAsync(
            $"/collections/{_collectionName}/points/scroll",
            request,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var scrollResponse = await response.Content.ReadFromJsonAsync<QdrantScrollResponse>(
            JsonOptions,
            cancellationToken);

        return scrollResponse?.Result?.Points
            .Where(point => point.Payload is not null)
            .Select(point => new SearchResult(
                point.Payload!.DocumentId,
                point.Payload.ChunkIndex,
                1,
                point.Payload.Text,
                point.Payload.FileName))
            .ToArray() ?? [];
    }

    private async Task EnsureTextPayloadIndexAsync(CancellationToken cancellationToken)
    {
        var request = new CreateTextPayloadIndexRequest(
            "text",
            new TextPayloadSchema(
                "text",
                "word",
                true,
                2,
                20));

        using var response = await _httpClient.PutAsJsonAsync(
            $"/collections/{_collectionName}/index",
            request,
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode ||
            response.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.BadRequest)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private static string CreatePointId(Guid documentId, int chunkIndex)
    {
        var source = Encoding.UTF8.GetBytes($"{documentId:N}:{chunkIndex}");
        var hash = SHA256.HashData(source);
        var bytes = hash[..16].ToArray();

        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes).ToString();
    }

    private static IReadOnlyList<string> ExtractTextSearchTerms(string query)
    {
        var stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a",
            "an",
            "and",
            "are",
            "as",
            "be",
            "by",
            "do",
            "does",
            "for",
            "how",
            "in",
            "is",
            "it",
            "of",
            "on",
            "or",
            "that",
            "the",
            "this",
            "to",
            "what",
            "with",
            "why",
            "ve",
            "veya",
            "bir",
            "bu",
            "su",
            "icin",
            "ile",
            "mi",
            "mı",
            "mu",
            "mü",
            "da",
            "de",
            "ne",
            "nedir",
            "nasil",
            "nasıl",
            "olarak"
        };

        var separators = query
            .Where(character => !char.IsLetterOrDigit(character))
            .Distinct()
            .ToArray();

        return query
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .Where(term => term.Length >= 2)
            .Where(term => !stopwords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record CreateCollectionRequest(
        [property: JsonPropertyName("vectors")] VectorConfig Vectors);

    private sealed record VectorConfig(
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("distance")] string Distance);

    private sealed record UpsertPointsRequest(
        [property: JsonPropertyName("points")] IReadOnlyList<QdrantPoint> Points);

    private sealed record QdrantPoint(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("payload")] QdrantPayload Payload);

    private sealed record QdrantPayload(
        [property: JsonPropertyName("documentId")]
        string DocumentId,
        [property: JsonPropertyName("chunkIndex")]
        int ChunkIndex,
        [property: JsonPropertyName("text")]
        string Text,
        [property: JsonPropertyName("fileName")]
        string FileName,
        [property: JsonPropertyName("contentType")]
        string ContentType,
        [property: JsonPropertyName("createdAt")]
        DateTime CreatedAt);

    private sealed record SearchPointsRequest(
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload);

    private sealed record QdrantSearchResponse(
        [property: JsonPropertyName("result")] IReadOnlyList<QdrantScoredPoint> Result);

    private sealed record QdrantScoredPoint(
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("payload")] QdrantPayload? Payload);

    private sealed record CreateTextPayloadIndexRequest(
        [property: JsonPropertyName("field_name")] string FieldName,
        [property: JsonPropertyName("field_schema")] TextPayloadSchema FieldSchema);

    private sealed record TextPayloadSchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("tokenizer")] string Tokenizer,
        [property: JsonPropertyName("lowercase")] bool Lowercase,
        [property: JsonPropertyName("min_token_len")] int MinTokenLen,
        [property: JsonPropertyName("max_token_len")] int MaxTokenLen);

    private sealed record ScrollPointsRequest(
        [property: JsonPropertyName("filter")] QdrantFilter Filter,
        [property: JsonPropertyName("limit")] int Limit,
        [property: JsonPropertyName("with_payload")] bool WithPayload);

    private sealed record QdrantFilter(
        [property: JsonPropertyName("must")] IReadOnlyList<TextMatchCondition> Must);

    private sealed record TextMatchCondition(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("match")] TextMatch Match);

    private sealed record TextMatch(
        [property: JsonPropertyName("text")] string Text);

    private sealed record QdrantScrollResponse(
        [property: JsonPropertyName("result")] QdrantScrollResult Result);

    private sealed record QdrantScrollResult(
        [property: JsonPropertyName("points")] IReadOnlyList<QdrantPointResult> Points);

    private sealed record QdrantPointResult(
        [property: JsonPropertyName("payload")] QdrantPayload? Payload);
}
