using AiKnowledgePlatform.Api.Features.Documents;
using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Features.Search;
using AiKnowledgePlatform.Api.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Tests;

public sealed class HybridSearchServiceTests
{
    [Theory]
    [InlineData("AOF nedir?", "AOF")]
    [InlineData("Redis persistence nedir?", "Redis", "persistence")]
    [InlineData("RabbitMQ DLQ nasıl çalışır?", "RabbitMQ", "DLQ")]
    public void ExtractSearchTerms_RemovesStopwordsAndPreservesTechnicalTerms(
        string question,
        params string[] expectedTerms)
    {
        var terms = LexicalSearchService.ExtractSearchTerms(question);

        Assert.Equal(expectedTerms, terms);
    }

    [Fact]
    public async Task SearchAsync_WhenHybridDisabled_UsesSemanticOnly()
    {
        var semanticResult = CreateResult("semantic.pdf", 0, 0.8, "Semantic result");
        var lexicalResult = CreateResult("lexical.pdf", 1, 0, "Lexical result");
        var service = CreateService(
            enabled: false,
            semanticResults: [semanticResult],
            lexicalResults: [lexicalResult]);

        var results = await service.SearchAsync("Redis persistence", 20);

        Assert.Single(results);
        Assert.Equal("semantic.pdf", results[0].FileName);
    }

    [Fact]
    public async Task SearchAsync_MergesAndDeduplicatesResults()
    {
        var documentId = Guid.NewGuid().ToString();
        var semanticResult = new SearchResult(documentId, 0, 0.8, "Redis persistence", "redis.pdf");
        var lexicalResult = new SearchResult(documentId, 0, 0, "Redis persistence", "redis.pdf");
        var service = CreateService(
            enabled: true,
            semanticResults: [semanticResult],
            lexicalResults: [lexicalResult]);

        var results = await service.SearchAsync("Redis persistence", 20);

        Assert.Single(results);
        Assert.Equal(documentId, results[0].DocumentId);
        Assert.Equal(2.8, results[0].Score);
    }

    [Fact]
    public async Task SearchAsync_IncludesLexicalOnlyCandidates()
    {
        var semanticResult = CreateResult("semantic.pdf", 0, 0.8, "Semantic result");
        var lexicalResult = CreateResult("lexical.pdf", 1, 0, "Lexical result");
        var service = CreateService(
            enabled: true,
            semanticResults: [semanticResult],
            lexicalResults: [lexicalResult]);

        var results = await service.SearchAsync("Redis persistence", 20);

        Assert.Contains(results, result => result.FileName == "semantic.pdf");
        Assert.Contains(results, result => result.FileName == "lexical.pdf");
    }

    [Fact]
    public async Task SearchAsync_UsesExtractedLexicalTerms()
    {
        var semanticResult = CreateResult("semantic.pdf", 0, 0.8, "Semantic result");
        var lexicalResult = CreateResult("aof.pdf", 1, 0, "AOF persistence result");
        var qdrantClient = new FakeQdrantClient([semanticResult], [lexicalResult]);
        var service = CreateService(
            enabled: true,
            qdrantClient);

        var results = await service.SearchAsync("AOF nedir?", 20);

        Assert.Contains(results, result => result.FileName == "aof.pdf");
        Assert.Equal(["AOF"], qdrantClient.TextSearchQueries);
    }

    private static HybridSearchService CreateService(
        bool enabled,
        IReadOnlyList<SearchResult> semanticResults,
        IReadOnlyList<SearchResult> lexicalResults)
    {
        var qdrantClient = new FakeQdrantClient(semanticResults, lexicalResults);
        return CreateService(enabled, qdrantClient);
    }

    private static HybridSearchService CreateService(
        bool enabled,
        FakeQdrantClient qdrantClient)
    {
        var semanticSearchService = new SemanticSearchService(new FakeOllamaEmbeddingGenerator(), qdrantClient);
        var lexicalSearchService = new LexicalSearchService(
            qdrantClient,
            NullLogger<LexicalSearchService>.Instance);
        var options = Options.Create(new HybridSearchOptions
        {
            Enabled = enabled,
            SemanticCandidateCount = 20,
            LexicalCandidateCount = 20,
            FinalCandidateCount = 20
        });

        return new HybridSearchService(
            semanticSearchService,
            lexicalSearchService,
            options,
            NullLogger<HybridSearchService>.Instance);
    }

    private static SearchResult CreateResult(string fileName, int chunkIndex, double score, string text)
    {
        return new SearchResult(Guid.NewGuid().ToString(), chunkIndex, score, text, fileName);
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
    }

    private sealed class FakeQdrantClient : QdrantClient
    {
        private readonly IReadOnlyList<SearchResult> _semanticResults;
        private readonly IReadOnlyList<SearchResult> _lexicalResults;

        public FakeQdrantClient(
            IReadOnlyList<SearchResult> semanticResults,
            IReadOnlyList<SearchResult> lexicalResults)
            : base(new HttpClient(), CreateConfiguration())
        {
            _semanticResults = semanticResults;
            _lexicalResults = lexicalResults;
        }

        public List<string> TextSearchQueries { get; } = [];

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
            return Task.FromResult<IReadOnlyList<SearchResult>>(_semanticResults.Take(topK).ToArray());
        }

        public override Task<IReadOnlyList<SearchResult>> SearchByTextAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            TextSearchQueries.Add(query);

            return Task.FromResult<IReadOnlyList<SearchResult>>(_lexicalResults.Take(limit).ToArray());
        }
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:EmbeddingModel"] = "nomic-embed-text",
                ["Qdrant:BaseUrl"] = "http://localhost:6333",
                ["Qdrant:CollectionName"] = "document_chunks",
                ["Qdrant:VectorSize"] = "768"
            })
            .Build();
    }
}
