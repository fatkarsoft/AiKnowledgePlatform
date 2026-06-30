using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Tests;

public sealed class KeywordOverlapRerankerTests
{
    private readonly KeywordOverlapReranker _reranker = new();

    [Fact]
    public async Task RerankAsync_CandidateWithMoreQuestionKeywordOverlap_RanksHigher()
    {
        var request = new RerankRequest(
            "Redis cache nedir?",
            [
                CreateResult(0, 0.95, "RabbitMQ is a message broker.", "rabbitmq.pdf"),
                CreateResult(1, 0.10, "Redis is an in-memory cache and data store.", "redis.pdf")
            ],
            2);

        var results = await _reranker.RerankAsync(request);

        Assert.Equal("redis.pdf", results[0].FileName);
    }

    [Fact]
    public async Task RerankAsync_RespectsTopN()
    {
        var request = new RerankRequest(
            "Redis cache replication",
            [
                CreateResult(0, 0.10, "Redis cache details.", "cache.pdf"),
                CreateResult(1, 0.20, "Redis replication details.", "replication.pdf"),
                CreateResult(2, 0.30, "Redis cluster details.", "cluster.pdf")
            ],
            2);

        var results = await _reranker.RerankAsync(request);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task RerankAsync_PreservesOriginalScore()
    {
        var request = new RerankRequest(
            "Redis cache",
            [CreateResult(0, 0.42, "Redis cache details.", "redis.pdf")],
            1);

        var results = await _reranker.RerankAsync(request);

        Assert.Equal(0.42, results[0].OriginalScore);
    }

    [Fact]
    public async Task RerankAsync_MatchesCaseInsensitively()
    {
        var request = new RerankRequest(
            "redis CACHE",
            [
                CreateResult(0, 0.99, "Unrelated queue details.", "queue.pdf"),
                CreateResult(1, 0.01, "REDIS cache details.", "redis.pdf")
            ],
            2);

        var results = await _reranker.RerankAsync(request);

        Assert.Equal("redis.pdf", results[0].FileName);
    }

    private static SearchResult CreateResult(int chunkIndex, double score, string text, string fileName)
    {
        return new SearchResult(Guid.NewGuid().ToString(), chunkIndex, score, text, fileName);
    }
}
