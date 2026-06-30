using System.Text.Json;
using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Storage;

namespace AiKnowledgePlatform.Api.Features.Documents;

public static class DocumentsEndpoint
{
    public static IEndpointRouteBuilder MapDocumentsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/documents", async (
            IFormFile? file,
            IWebHostEnvironment environment,
            TextChunker textChunker,
            OllamaEmbeddingGenerator embeddingGenerator,
            QdrantClient qdrantClient) =>
        {
            if (file is null)
            {
                return Results.BadRequest("File is required.");
            }

            if (file.Length <= 0)
            {
                return Results.BadRequest("File must not be empty.");
            }

            if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("File content type must be application/pdf.");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("File extension must be .pdf.");
            }

            var documentId = Guid.NewGuid();
            var documentDirectory = Path.Combine(environment.ContentRootPath, "storage", "documents", documentId.ToString());
            Directory.CreateDirectory(documentDirectory);

            var filePath = Path.Combine(documentDirectory, "original.pdf");
            await using (var stream = File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            var metadata = await DocumentMetadataWriter.WriteAsync(documentDirectory, documentId, file);

            try
            {
                var text = PdfTextExtractor.ExtractText(filePath);
                var chunks = textChunker.CreateChunks(documentId, text);
                await SaveChunksAsync(documentDirectory, chunks);

                metadata = metadata with { Status = DocumentStatus.Chunked };
                await DocumentMetadataWriter.WriteAsync(documentDirectory, metadata);

                var embeddings = await SaveEmbeddingsAsync(documentDirectory, embeddingGenerator);

                metadata = metadata with { Status = DocumentStatus.Embedded };
                await DocumentMetadataWriter.WriteAsync(documentDirectory, metadata);

                await qdrantClient.EnsureCollectionAsync();
                await qdrantClient.UpsertChunksAsync(metadata, chunks, embeddings);

                metadata = metadata with { Status = DocumentStatus.Indexed };
                await DocumentMetadataWriter.WriteAsync(documentDirectory, metadata);
            }
            catch
            {
                metadata = metadata with { Status = DocumentStatus.Failed };
                await DocumentMetadataWriter.WriteAsync(documentDirectory, metadata);
            }

            return Results.Ok(new UploadDocumentResponse(
                metadata.DocumentId.ToString(),
                metadata.FileName,
                metadata.ContentType,
                metadata.Size,
                metadata.Status));
        })
        .WithName("UploadDocument")
        .WithTags("Documents")
        .WithSummary("Upload a PDF document")
        .WithDescription("Uploads a PDF file, extracts text, creates chunks, and generates local embeddings.")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<UploadDocumentResponse>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest)
        .DisableAntiforgery();

        app.MapGet("/documents/{documentId:guid}", async (Guid documentId, IWebHostEnvironment environment) =>
        {
            var metadataPath = Path.Combine(
                environment.ContentRootPath,
                "storage",
                "documents",
                documentId.ToString(),
                "metadata.json");

            if (!File.Exists(metadataPath))
            {
                return Results.NotFound();
            }

            await using var stream = File.OpenRead(metadataPath);
            var metadata = await JsonSerializer.DeserializeAsync<DocumentMetadata>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            return metadata is null
                ? Results.NotFound()
                : Results.Ok(metadata);
        })
        .WithName("GetDocument")
        .WithTags("Documents")
        .WithSummary("Get document metadata")
        .WithDescription("Returns metadata and lifecycle status for a previously uploaded document.")
        .Produces<DocumentMetadata>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/documents/{documentId:guid}/chunks", async (Guid documentId, IWebHostEnvironment environment) =>
        {
            var documentDirectory = Path.Combine(
                environment.ContentRootPath,
                "storage",
                "documents",
                documentId.ToString());

            if (!Directory.Exists(documentDirectory))
            {
                return Results.NotFound();
            }

            var chunksDirectory = Path.Combine(documentDirectory, "chunks");
            if (!Directory.Exists(chunksDirectory))
            {
                return Results.Ok(Array.Empty<DocumentChunk>());
            }

            var chunks = new List<DocumentChunk>();
            foreach (var chunkPath in Directory.GetFiles(chunksDirectory, "chunk-*.json"))
            {
                await using var stream = File.OpenRead(chunkPath);
                var chunk = await JsonSerializer.DeserializeAsync<DocumentChunk>(
                    stream,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (chunk is not null)
                {
                    chunks.Add(chunk);
                }
            }

            return Results.Ok(chunks.OrderBy(chunk => chunk.Index));
        })
        .WithName("GetDocumentChunks")
        .WithTags("Documents")
        .WithSummary("Get document chunks")
        .WithDescription("Returns generated chunks for a document.")
        .Produces<IReadOnlyList<DocumentChunk>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task SaveChunksAsync(string documentDirectory, IReadOnlyList<DocumentChunk> chunks)
    {
        var chunksDirectory = Path.Combine(documentDirectory, "chunks");
        Directory.CreateDirectory(chunksDirectory);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        foreach (var chunk in chunks)
        {
            var chunkPath = Path.Combine(chunksDirectory, $"chunk-{chunk.Index}.json");
            await using var stream = File.Create(chunkPath);
            await JsonSerializer.SerializeAsync(stream, chunk, jsonOptions);
        }
    }

    private static async Task<IReadOnlyList<ChunkEmbedding>> SaveEmbeddingsAsync(
        string documentDirectory,
        OllamaEmbeddingGenerator embeddingGenerator)
    {
        var chunksDirectory = Path.Combine(documentDirectory, "chunks");
        var embeddingsDirectory = Path.Combine(documentDirectory, "embeddings");
        Directory.CreateDirectory(embeddingsDirectory);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        var chunks = new List<DocumentChunk>();
        foreach (var chunkPath in Directory.GetFiles(chunksDirectory, "chunk-*.json"))
        {
            await using var stream = File.OpenRead(chunkPath);
            var chunk = await JsonSerializer.DeserializeAsync<DocumentChunk>(stream, jsonOptions)
                ?? throw new InvalidOperationException($"Could not read chunk file: {chunkPath}");

            chunks.Add(chunk);
        }

        var embeddings = new List<ChunkEmbedding>();
        foreach (var chunk in chunks.OrderBy(chunk => chunk.Index))
        {
            var vector = await embeddingGenerator.GenerateEmbeddingAsync(chunk.Text);
            var embedding = new ChunkEmbedding(
                chunk.DocumentId,
                chunk.Index,
                embeddingGenerator.Model,
                vector.Length,
                vector,
                DateTime.UtcNow);

            var embeddingPath = Path.Combine(embeddingsDirectory, $"chunk-{chunk.Index}.embedding.json");
            await using var stream = File.Create(embeddingPath);
            await JsonSerializer.SerializeAsync(stream, embedding, jsonOptions);
            embeddings.Add(embedding);
        }

        return embeddings;
    }
}
