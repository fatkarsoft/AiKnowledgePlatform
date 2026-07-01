using System.Text.Json;
using AiKnowledgePlatform.Api.Storage;

namespace AiKnowledgePlatform.Api.Features.Debug;

public static class QdrantDebugEndpoint
{
    private const int TextPreviewMaxLength = 300;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapQdrantDebugEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/debug/qdrant/collection", async (
            QdrantClient qdrantClient,
            CancellationToken cancellationToken) =>
        {
            var rawResponse = await qdrantClient.GetCollectionRawAsync(cancellationToken);
            var raw = ParseJson(rawResponse);
            var payloadSchema = GetPayloadSchema(raw);
            var vectorConfiguration = GetVectorConfiguration(raw);
            var indexedPayloadFields = GetIndexedPayloadFields(payloadSchema);
            var textSchema = GetFieldSchema(payloadSchema, "text");

            return Results.Ok(new QdrantCollectionDebugResponse(
                qdrantClient.CollectionName,
                payloadSchema,
                indexedPayloadFields,
                textSchema.HasValue,
                textSchema.HasValue && IsFullTextIndex(textSchema.Value),
                vectorConfiguration,
                raw));
        })
        .WithName("DebugQdrantCollection")
        .WithTags("Debug")
        .WithSummary("Inspect Qdrant collection")
        .WithDescription("Returns Qdrant collection metadata, payload schema, indexed fields, text index status, and vector configuration.")
        .Produces<QdrantCollectionDebugResponse>(StatusCodes.Status200OK);

        app.MapGet("/debug/qdrant/text-search", async (
            string term,
            QdrantClient qdrantClient,
            CancellationToken cancellationToken,
            int? limit) =>
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return Results.BadRequest("Term is required.");
            }

            var debugResult = await qdrantClient.SearchByTextDebugAsync(
                term,
                NormalizeLimit(limit),
                cancellationToken);
            var raw = ParseJson(debugResult.RawResponseBody);
            var results = ExtractPayloadResults(raw);

            return Results.Ok(new QdrantTextSearchDebugResponse(
                term,
                SerializeRequest(debugResult.Request),
                debugResult.StatusCode,
                results.Count,
                results,
                raw));
        })
        .WithName("DebugQdrantTextSearch")
        .WithTags("Debug")
        .WithSummary("Inspect Qdrant text search")
        .WithDescription("Runs only Qdrant payload text match search and returns the exact request body plus raw response.")
        .Produces<QdrantTextSearchDebugResponse>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest);

        app.MapGet("/debug/qdrant/scroll", async (
            QdrantClient qdrantClient,
            CancellationToken cancellationToken,
            int? limit) =>
        {
            var debugResult = await qdrantClient.ScrollDebugAsync(
                NormalizeLimit(limit),
                cancellationToken);
            var raw = ParseJson(debugResult.RawResponseBody);
            var results = ExtractPayloadResults(raw);

            return Results.Ok(new QdrantScrollDebugResponse(
                NormalizeLimit(limit),
                SerializeRequest(debugResult.Request),
                debugResult.StatusCode,
                results.Count,
                results,
                raw));
        })
        .WithName("DebugQdrantScroll")
        .WithTags("Debug")
        .WithSummary("Inspect Qdrant stored payloads")
        .WithDescription("Scrolls Qdrant without semantic search, reranking, or LLM calls to show stored payload fields.")
        .Produces<QdrantScrollDebugResponse>(StatusCodes.Status200OK);

        app.MapGet("/debug/qdrant/indexes", async (
            QdrantClient qdrantClient,
            CancellationToken cancellationToken) =>
        {
            var rawResponse = await qdrantClient.GetCollectionRawAsync(cancellationToken);
            var raw = ParseJson(rawResponse);
            var payloadSchema = GetPayloadSchema(raw);
            var indexedPayloadFields = GetIndexedPayloadFields(payloadSchema);
            var textSchema = GetFieldSchema(payloadSchema, "text");

            return Results.Ok(new QdrantIndexesDebugResponse(
                qdrantClient.CollectionName,
                payloadSchema,
                indexedPayloadFields,
                textSchema.HasValue,
                textSchema.HasValue && IsFullTextIndex(textSchema.Value),
                raw));
        })
        .WithName("DebugQdrantIndexes")
        .WithTags("Debug")
        .WithSummary("Inspect Qdrant payload indexes")
        .WithDescription("Returns payload indexes exposed by Qdrant collection metadata.")
        .Produces<QdrantIndexesDebugResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static int NormalizeLimit(int? limit)
    {
        return Math.Max(1, limit.GetValueOrDefault(5));
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static JsonElement SerializeRequest(object request)
    {
        return JsonSerializer.SerializeToElement(request, JsonOptions);
    }

    private static JsonElement? GetPayloadSchema(JsonElement raw)
    {
        if (raw.TryGetProperty("result", out var result) &&
            result.TryGetProperty("payload_schema", out var payloadSchema))
        {
            return payloadSchema.Clone();
        }

        return null;
    }

    private static JsonElement? GetVectorConfiguration(JsonElement raw)
    {
        if (raw.TryGetProperty("result", out var result) &&
            result.TryGetProperty("config", out var config) &&
            config.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty("vectors", out var vectors))
        {
            return vectors.Clone();
        }

        return null;
    }

    private static IReadOnlyList<string> GetIndexedPayloadFields(JsonElement? payloadSchema)
    {
        if (!payloadSchema.HasValue || payloadSchema.Value.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return payloadSchema.Value
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static JsonElement? GetFieldSchema(JsonElement? payloadSchema, string fieldName)
    {
        if (payloadSchema.HasValue &&
            payloadSchema.Value.ValueKind == JsonValueKind.Object &&
            payloadSchema.Value.TryGetProperty(fieldName, out var fieldSchema))
        {
            return fieldSchema.Clone();
        }

        return null;
    }

    private static bool IsFullTextIndex(JsonElement fieldSchema)
    {
        var rawSchema = fieldSchema.GetRawText();

        return rawSchema.Contains("\"text\"", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<QdrantPayloadDebugResult> ExtractPayloadResults(JsonElement raw)
    {
        if (!raw.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("points", out var points) ||
            points.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return points
            .EnumerateArray()
            .Where(point => point.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.Object)
            .Select(point =>
            {
                var payload = point.GetProperty("payload");
                var text = GetString(payload, "text") ?? "";

                return new QdrantPayloadDebugResult(
                    GetString(payload, "documentId"),
                    GetInt(payload, "chunkIndex"),
                    GetString(payload, "fileName"),
                    CreatePreview(text),
                    payload.Clone());
            })
            .ToArray();
    }

    private static string? GetString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? GetInt(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static string CreatePreview(string text)
    {
        if (text.Length <= TextPreviewMaxLength)
        {
            return text;
        }

        return text[..TextPreviewMaxLength];
    }
}

public sealed record QdrantCollectionDebugResponse(
    string CollectionName,
    JsonElement? PayloadSchema,
    IReadOnlyList<string> IndexedPayloadFields,
    bool TextExists,
    bool TextHasFullTextIndex,
    JsonElement? VectorConfiguration,
    JsonElement RawResponse);

public sealed record QdrantTextSearchDebugResponse(
    string Term,
    JsonElement Request,
    int StatusCode,
    int Count,
    IReadOnlyList<QdrantPayloadDebugResult> Results,
    JsonElement RawResponse);

public sealed record QdrantScrollDebugResponse(
    int Limit,
    JsonElement Request,
    int StatusCode,
    int Count,
    IReadOnlyList<QdrantPayloadDebugResult> Results,
    JsonElement RawResponse);

public sealed record QdrantIndexesDebugResponse(
    string CollectionName,
    JsonElement? PayloadSchema,
    IReadOnlyList<string> IndexedPayloadFields,
    bool TextIndexed,
    bool TextHasFullTextIndex,
    JsonElement RawResponse);

public sealed record QdrantPayloadDebugResult(
    string? DocumentId,
    int? ChunkIndex,
    string? FileName,
    string TextPreview,
    JsonElement Payload);
