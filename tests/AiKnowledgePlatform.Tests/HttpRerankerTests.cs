using System.Net;
using System.Text;
using System.Text.Json;
using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Search;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Tests;

public sealed class HttpRerankerTests
{
    [Fact]
    public async Task RerankAsync_SendsQueryAndCandidateTexts()
    {
        var handler = new FakeHttpMessageHandler("""[{ "index": 0, "score": 0.7 }]""");
        var reranker = CreateReranker(handler);

        await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [
                CreateResult(0, 0.10, "Redis supports persistence using RDB snapshots and AOF.", "redis.pdf"),
                CreateResult(1, 0.20, "RabbitMQ is a message broker.", "rabbitmq.pdf")
            ],
            1));

        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("http://localhost:8081/rerank", handler.RequestUri?.ToString());

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;

        Assert.Equal("What is Redis persistence?", root.GetProperty("query").GetString());
        Assert.Equal("Redis supports persistence using RDB snapshots and AOF.", root.GetProperty("texts")[0].GetString());
        Assert.Equal("RabbitMQ is a message broker.", root.GetProperty("texts")[1].GetString());
    }

    [Fact]
    public async Task RerankAsync_MapsResponseIndexBackToCandidate()
    {
        var handler = new FakeHttpMessageHandler("""[{ "index": 1, "score": 0.9 }]""");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [
                CreateResult(0, 0.10, "RabbitMQ is a message broker.", "rabbitmq.pdf"),
                CreateResult(1, 0.20, "Redis supports persistence using RDB snapshots and AOF.", "redis.pdf")
            ],
            1));

        Assert.Single(results);
        Assert.Equal(1, results[0].ChunkIndex);
        Assert.Equal("redis.pdf", results[0].FileName);
    }

    [Fact]
    public async Task RerankAsync_OrdersByTeiScoreDescending()
    {
        var handler = new FakeHttpMessageHandler(
            """[{ "index": 0, "score": 0.1 }, { "index": 1, "score": 0.9 }]""");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [
                CreateResult(0, 0.99, "RabbitMQ is a message broker.", "rabbitmq.pdf"),
                CreateResult(1, 0.01, "Redis supports persistence using RDB snapshots and AOF.", "redis.pdf")
            ],
            2));

        Assert.Equal("redis.pdf", results[0].FileName);
        Assert.Equal("rabbitmq.pdf", results[1].FileName);
    }

    [Fact]
    public async Task RerankAsync_RespectsTopN()
    {
        var handler = new FakeHttpMessageHandler(
            """[{ "index": 0, "score": 0.8 }, { "index": 1, "score": 0.7 }]""");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [
                CreateResult(0, 0.10, "Redis supports persistence using RDB snapshots.", "rdb.pdf"),
                CreateResult(1, 0.20, "Redis supports AOF persistence.", "aof.pdf")
            ],
            1));

        Assert.Single(results);
    }

    [Fact]
    public async Task RerankAsync_PreservesOriginalScore()
    {
        var handler = new FakeHttpMessageHandler("""[{ "index": 0, "score": 0.8 }]""");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [CreateResult(0, 0.42, "Redis supports persistence using RDB snapshots.", "redis.pdf")],
            1));

        Assert.Equal(0.42, results[0].OriginalScore);
        Assert.Equal(0.8, results[0].RerankScore);
    }

    [Fact]
    public async Task RerankAsync_IgnoresInvalidTeiIndex()
    {
        var handler = new FakeHttpMessageHandler(
            """[{ "index": 99, "score": 0.95 }, { "index": 0, "score": 0.8 }]""");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [CreateResult(0, 0.42, "Redis supports persistence using RDB snapshots.", "redis.pdf")],
            2));

        Assert.Single(results);
        Assert.Equal("redis.pdf", results[0].FileName);
    }

    [Fact]
    public async Task RerankAsync_EmptyTeiResponse_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler("[]");
        var reranker = CreateReranker(handler);

        var results = await reranker.RerankAsync(new RerankRequest(
            "What is Redis persistence?",
            [CreateResult(0, 0.42, "Redis supports persistence using RDB snapshots.", "redis.pdf")],
            1));

        Assert.Empty(results);
    }

    private static HttpReranker CreateReranker(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new RerankerServiceOptions());

        return new HttpReranker(httpClient, options);
    }

    private static SearchResult CreateResult(int chunkIndex, double score, string text, string fileName)
    {
        return new SearchResult(Guid.NewGuid().ToString(), chunkIndex, score, text, fileName);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public FakeHttpMessageHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
