using System.Text.Json;
using AiKnowledgePlatform.Api.Features.Documents;

namespace AiKnowledgePlatform.Api.Storage;

public static class DocumentMetadataWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<DocumentMetadata> WriteAsync(
        string documentDirectory,
        Guid documentId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var metadata = new DocumentMetadata(
            documentId,
            file.FileName,
            file.ContentType,
            file.Length,
            DocumentStatus.Uploaded,
            DateTime.UtcNow);

        await WriteAsync(documentDirectory, metadata, cancellationToken);

        return metadata;
    }

    public static async Task WriteAsync(
        string documentDirectory,
        DocumentMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var metadataPath = Path.Combine(documentDirectory, "metadata.json");
        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
    }
}
