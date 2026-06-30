using AiKnowledgePlatform.Api.AI;
using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public void Build_IncludesQuestion()
    {
        var prompt = new PromptBuilder().Build(
            "Redis nedir?",
            CreateSearchResults());

        Assert.Contains("Redis nedir?", prompt);
    }

    [Fact]
    public void Build_SeparatesContextChunks()
    {
        var prompt = new PromptBuilder().Build(
            "Redis nedir?",
            CreateSearchResults());

        Assert.Contains("--- Context Chunk 0 ---", prompt);
        Assert.Contains("--- Context Chunk 1 ---", prompt);
    }

    [Fact]
    public void Build_IncludesContextOnlyInstruction()
    {
        var prompt = new PromptBuilder().Build(
            "Redis nedir?",
            CreateSearchResults());

        Assert.Contains("Answer only using the provided context", prompt);
        Assert.Contains("Sağlanan bağlamda bu soruyu cevaplamak için yeterli bilgi yok.", prompt);
    }

    [Fact]
    public void Build_IncludesNaturalLanguageInstruction()
    {
        var prompt = new PromptBuilder().Build(
            "Message broker nedir?",
            CreateSearchResults());

        Assert.Contains("Answer in the same language as the user's question", prompt);
        Assert.Contains("clear and natural Turkish", prompt);
    }

    [Fact]
    public void Build_IncludesTechnicalTermPreservationInstruction()
    {
        var prompt = new PromptBuilder().Build(
            "Message broker nedir?",
            CreateSearchResults());

        Assert.Contains("Keep technical terms in English", prompt);
        Assert.Contains("message broker", prompt);
        Assert.Contains("dead letter queue", prompt);
    }

    private static IReadOnlyList<SearchResult> CreateSearchResults()
    {
        return
        [
            new SearchResult(Guid.NewGuid().ToString(), 0, 0.93, "Redis stores data in memory.", "redis.pdf"),
            new SearchResult(Guid.NewGuid().ToString(), 1, 0.87, "Redis can be used as a cache.", "redis.pdf")
        ];
    }
}
