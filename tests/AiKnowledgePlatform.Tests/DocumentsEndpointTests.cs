using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AiKnowledgePlatform.Api.AI;
using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Documents;
using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Features.Chat;
using AiKnowledgePlatform.Api.Features.Search;
using AiKnowledgePlatform.Api.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiKnowledgePlatform.Tests;

public sealed class DocumentsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DocumentsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<OllamaEmbeddingGenerator>();
                services.RemoveAll<OllamaChatClient>();
                services.RemoveAll<QdrantClient>();
                services.RemoveAll<IReranker>();
                services.AddSingleton<OllamaEmbeddingGenerator>(new FakeOllamaEmbeddingGenerator());
                services.AddSingleton<OllamaChatClient>(new FakeOllamaChatClient());
                services.AddSingleton<QdrantClient>(new FakeQdrantClient());
                services.AddSingleton<IReranker, KeywordOverlapReranker>();
            });
        });
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_ReturnsOk()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for chunking."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var response = await client.PostAsync("/documents", form);
        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(Guid.TryParse(body.DocumentId, out _));
        Assert.Equal("sample.pdf", body.FileName);
        Assert.Equal("application/pdf", body.ContentType);
        Assert.Equal(fileContent.Headers.ContentLength, body.Size);
        Assert.Equal("Indexed", body.Status);
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_WritesMetadataFile()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for metadata."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var response = await client.PostAsync("/documents", form);
        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        var environment = _factory.Services.GetRequiredService<IWebHostEnvironment>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);

        var metadataPath = Path.Combine(
            environment.ContentRootPath,
            "storage",
            "documents",
            body.DocumentId,
            "metadata.json");

        Assert.True(File.Exists(metadataPath));
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_CreatesChunksFolder()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for chunks folder."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var response = await client.PostAsync("/documents", form);
        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        var environment = _factory.Services.GetRequiredService<IWebHostEnvironment>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);

        var chunksPath = Path.Combine(
            environment.ContentRootPath,
            "storage",
            "documents",
            body.DocumentId,
            "chunks");

        Assert.True(Directory.Exists(chunksPath));
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_CreatesAtLeastOneChunkFile()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for chunk file."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var response = await client.PostAsync("/documents", form);
        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        var environment = _factory.Services.GetRequiredService<IWebHostEnvironment>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);

        var chunksPath = Path.Combine(
            environment.ContentRootPath,
            "storage",
            "documents",
            body.DocumentId,
            "chunks");

        Assert.NotEmpty(Directory.GetFiles(chunksPath, "chunk-*.json"));
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_CreatesEmbeddingFiles()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for embedding file."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var response = await client.PostAsync("/documents", form);
        var body = await response.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();
        var environment = _factory.Services.GetRequiredService<IWebHostEnvironment>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);

        var embeddingsPath = Path.Combine(
            environment.ContentRootPath,
            "storage",
            "documents",
            body.DocumentId,
            "embeddings");

        Assert.NotEmpty(Directory.GetFiles(embeddingsPath, "chunk-*.embedding.json"));
    }

    [Fact]
    public async Task UploadDocument_WithValidPdf_UpdatesMetadataStatusToIndexed()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for embedded metadata."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var uploadResponse = await client.PostAsync("/documents", form);
        var uploadedDocument = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploadedDocument);

        var response = await client.GetAsync($"/documents/{uploadedDocument.DocumentId}");
        var metadata = await response.Content.ReadFromJsonAsync<DocumentMetadataDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(metadata);
        Assert.Equal("Indexed", metadata.Status);
    }

    [Fact]
    public async Task GetDocument_WithExistingMetadata_ReturnsOk()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for metadata lookup."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var uploadResponse = await client.PostAsync("/documents", form);
        var uploadedDocument = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploadedDocument);

        var response = await client.GetAsync($"/documents/{uploadedDocument.DocumentId}");
        var metadata = await response.Content.ReadFromJsonAsync<DocumentMetadataDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(metadata);
        Assert.Equal(uploadedDocument.DocumentId, metadata.DocumentId);
        Assert.Equal("sample.pdf", metadata.FileName);
        Assert.Equal("application/pdf", metadata.ContentType);
        Assert.Equal(uploadedDocument.Size, metadata.Size);
        Assert.Equal("Indexed", metadata.Status);
    }

    [Fact]
    public async Task GetDocument_WithUnknownDocument_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentChunks_WithExistingChunks_ReturnsOk()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(CreatePdfBytes("Sample PDF text for chunk lookup."));
        fileContent.Headers.ContentType = new("application/pdf");
        form.Add(fileContent, "file", "sample.pdf");

        var uploadResponse = await client.PostAsync("/documents", form);
        var uploadedDocument = await uploadResponse.Content.ReadFromJsonAsync<UploadDocumentResponseDto>();

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Assert.NotNull(uploadedDocument);

        var response = await client.GetAsync($"/documents/{uploadedDocument.DocumentId}/chunks");
        var chunks = await response.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chunks);
        Assert.NotEmpty(chunks);
        Assert.Equal(chunks.OrderBy(chunk => chunk.Index).Select(chunk => chunk.Index), chunks.Select(chunk => chunk.Index));
    }

    [Fact]
    public async Task GetDocumentChunks_WithUnknownDocument_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/documents/{Guid.NewGuid()}/chunks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentChunks_WithExistingDocumentWithoutChunks_ReturnsEmptyArray()
    {
        var client = _factory.CreateClient();
        var environment = _factory.Services.GetRequiredService<IWebHostEnvironment>();
        var documentId = Guid.NewGuid();
        var documentDirectory = Path.Combine(
            environment.ContentRootPath,
            "storage",
            "documents",
            documentId.ToString());

        Directory.CreateDirectory(documentDirectory);
        var metadata = new DocumentMetadata(
            documentId,
            "sample.pdf",
            "application/pdf",
            123,
            DocumentStatus.Uploaded,
            DateTime.UtcNow);

        await File.WriteAllTextAsync(
            Path.Combine(documentDirectory, "metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var response = await client.GetAsync($"/documents/{documentId}/chunks");
        var chunks = await response.Content.ReadFromJsonAsync<List<DocumentChunkDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chunks);
        Assert.Empty(chunks);
    }

    [Fact]
    public async Task Search_WithoutQuery_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/search", new SearchRequest("", 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithValidQuery_ReturnsResults()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/search", new SearchRequest("Redis nedir?", null));
        var results = await response.Content.ReadFromJsonAsync<List<SearchResult>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("sample.pdf", results[0].FileName);
    }

    [Fact]
    public async Task Chat_WithoutQuestion_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/chat", new ChatRequest("", 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Chat_WithValidQuestion_ReturnsAnswerAndSources()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/chat", new ChatRequest("Redis nedir?", null));
        var chatResponse = await response.Content.ReadFromJsonAsync<ChatResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chatResponse);
        Assert.Equal("Redis bellek ici veri deposudur.", chatResponse.Answer);
        Assert.Single(chatResponse.Sources);
        Assert.Equal("sample.pdf", chatResponse.Sources[0].FileName);
    }

    [Fact]
    public async Task Chat_WithRerankingEnabled_ReturnsRerankedFinalSources()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<QdrantClient>();
                services.AddSingleton<QdrantClient>(new MultipleResultsQdrantClient());
            });
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/chat", new ChatRequest("Redis cache nedir?", 1));
        var chatResponse = await response.Content.ReadFromJsonAsync<ChatResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chatResponse);
        Assert.Single(chatResponse.Sources);
        Assert.Equal("redis.pdf", chatResponse.Sources[0].FileName);
    }

    [Fact]
    public async Task Chat_WithNoSearchResults_DoesNotCallOllama()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<QdrantClient>();
                services.RemoveAll<OllamaChatClient>();
                services.AddSingleton<QdrantClient>(new EmptyQdrantClient());
                services.AddSingleton<OllamaChatClient>(new ThrowingOllamaChatClient());
            });
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/chat", new ChatRequest("Unknown?", 5));
        var chatResponse = await response.Content.ReadFromJsonAsync<ChatResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(chatResponse);
        Assert.Equal("Sağlanan bağlamda bu soruyu cevaplamak için yeterli bilgi yok.", chatResponse.Answer);
        Assert.Empty(chatResponse.Sources);
    }

    [Fact]
    public async Task UploadDocument_WithoutFile_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadDocument_WithNonPdf_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        using var fileContent = new StringContent("plain text");
        fileContent.Headers.ContentType = new("text/plain");
        form.Add(fileContent, "file", "sample.txt");

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record UploadDocumentResponseDto(
        string DocumentId,
        string FileName,
        string ContentType,
        long Size,
        string Status);

    private sealed record DocumentMetadataDto(
        string DocumentId,
        string FileName,
        string ContentType,
        long Size,
        string Status,
        DateTime UploadedAt);

    private sealed record DocumentChunkDto(
        string DocumentId,
        int Index,
        string Text,
        int CharacterCount,
        DateTime CreatedAt);

    private static byte[] CreatePdfBytes(string text)
    {
        var escapedText = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = $"BT /F1 24 Tf 100 700 Td ({escapedText}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"
        };

        var builder = new StringBuilder();
        var offsets = new List<int>();

        builder.Append("%PDF-1.4\n");
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(i + 1);
            builder.Append(" 0 obj\n");
            builder.Append(objects[i]);
            builder.Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n");
        builder.Append($"0 {objects.Length + 1}\n");
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10"));
            builder.Append(" 00000 n \n");
        }

        builder.Append("trailer\n");
        builder.Append($"<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        builder.Append("startxref\n");
        builder.Append(xrefOffset);
        builder.Append("\n%%EOF");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class FakeOllamaEmbeddingGenerator : OllamaEmbeddingGenerator
    {
        public FakeOllamaEmbeddingGenerator()
            : base(new HttpClient(), CreateConfiguration())
        {
        }

        public override Task<float[]> GenerateEmbeddingAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Ollama:BaseUrl"] = "http://localhost:11434",
                    ["Ollama:EmbeddingModel"] = "nomic-embed-text"
                })
                .Build();
        }
    }

    private sealed class FakeOllamaChatClient : OllamaChatClient
    {
        public FakeOllamaChatClient()
            : base(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new OllamaChatOptions()))
        {
        }

        public override Task<string> GenerateAnswerAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Redis bellek ici veri deposudur.");
        }
    }

    private sealed class ThrowingOllamaChatClient : OllamaChatClient
    {
        public ThrowingOllamaChatClient()
            : base(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new OllamaChatOptions()))
        {
        }

        public override Task<string> GenerateAnswerAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Chat client should not be called.");
        }
    }

    private class FakeQdrantClient : QdrantClient
    {
        private static readonly string SampleDocumentId = Guid.NewGuid().ToString();

        public FakeQdrantClient()
            : base(new HttpClient(), CreateConfiguration())
        {
        }

        public override Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public override Task UpsertChunksAsync(
            DocumentMetadata metadata,
            IReadOnlyList<DocumentChunk> chunks,
            IReadOnlyList<ChunkEmbedding> embeddings,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public override Task<IReadOnlyList<SearchResult>> SearchAsync(
            float[] vector,
            int topK,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new SearchResult(SampleDocumentId, 0, 0.93, "Redis is an in-memory data store.", "sample.pdf")
            ];

            return Task.FromResult(results);
        }

        public override Task<IReadOnlyList<SearchResult>> SearchByTextAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new SearchResult(SampleDocumentId, 0, 0, "Redis is an in-memory data store.", "sample.pdf")
            ];

            return Task.FromResult<IReadOnlyList<SearchResult>>(results.Take(limit).ToArray());
        }

        private static IConfiguration CreateConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Qdrant:BaseUrl"] = "http://localhost:6333",
                    ["Qdrant:CollectionName"] = "document_chunks",
                    ["Qdrant:VectorSize"] = "768"
                })
                .Build();
        }
    }

    private sealed class EmptyQdrantClient : FakeQdrantClient
    {
        public override Task<IReadOnlyList<SearchResult>> SearchAsync(
            float[] vector,
            int topK,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }

        public override Task<IReadOnlyList<SearchResult>> SearchByTextAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        }
    }

    private sealed class MultipleResultsQdrantClient : FakeQdrantClient
    {
        public override Task<IReadOnlyList<SearchResult>> SearchAsync(
            float[] vector,
            int topK,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SearchResult> results =
            [
                new SearchResult(Guid.NewGuid().ToString(), 0, 0.95, "RabbitMQ is a message broker.", "rabbitmq.pdf"),
                new SearchResult(Guid.NewGuid().ToString(), 1, 0.10, "Redis is an in-memory cache and data store.", "redis.pdf")
            ];

            return Task.FromResult<IReadOnlyList<SearchResult>>(results.Take(topK).ToArray());
        }
    }
}
