using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using Microsoft.Extensions.Options;

namespace AiKnowledgePlatform.Tests;

public sealed class TextChunkerTests
{
    [Fact]
    public void CreateChunks_OverlapStartsAtSentenceBoundary()
    {
        var documentId = Guid.NewGuid();
        var firstSentence = BuildSentence("First sentence", 420);
        var secondSentence = BuildSentence("Second sentence", 420);
        var thirdSentence = BuildSentence("Third sentence", 220);
        var text = $"{firstSentence} {secondSentence} {thirdSentence}";

        var chunks = CreateChunker().CreateChunks(documentId, text);

        Assert.True(chunks.Count >= 2);
        Assert.StartsWith(secondSentence, chunks[1].Text);
    }

    [Fact]
    public void CreateChunks_DoesNotSplitSentencesUnnecessarily()
    {
        var documentId = Guid.NewGuid();
        var firstSentence = BuildSentence("First sentence", 330);
        var secondSentence = BuildSentence("Second sentence", 330);
        var thirdSentence = BuildSentence("Third sentence", 330);
        var fourthSentence = BuildSentence("Fourth sentence", 180);
        var text = $"{firstSentence} {secondSentence} {thirdSentence} {fourthSentence}";

        var chunks = CreateChunker().CreateChunks(documentId, text);
        var joinedChunks = string.Join("\n", chunks.Select(chunk => chunk.Text));

        Assert.Contains(firstSentence, joinedChunks);
        Assert.Contains(secondSentence, joinedChunks);
        Assert.Contains(thirdSentence, joinedChunks);
        Assert.Contains(fourthSentence, joinedChunks);
    }

    [Fact]
    public void CreateChunks_PreservesParagraphBoundaries()
    {
        var documentId = Guid.NewGuid();
        var firstParagraph = "First paragraph keeps its own idea.";
        var secondParagraph = "Second paragraph starts after a blank line.";
        var text = $"{firstParagraph}\n\n{secondParagraph}";

        var chunks = CreateChunker().CreateChunks(documentId, text);
        var normalizedChunkText = chunks[0].Text.Replace("\r\n", "\n");

        Assert.Single(chunks);
        Assert.Contains($"{firstParagraph}\n\n{secondParagraph}", normalizedChunkText);
    }

    [Fact]
    public void CreateChunks_LongParagraphStillProducesMultipleChunks()
    {
        var documentId = Guid.NewGuid();
        var text = string.Join(' ', Enumerable.Repeat("word", 350));

        var chunks = CreateChunker().CreateChunks(documentId, text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
    }

    private static string BuildSentence(string prefix, int targetLength)
    {
        var words = new List<string> { prefix };

        while (string.Join(' ', words).Length < targetLength - 1)
        {
            words.Add("context");
        }

        return string.Join(' ', words)[..(targetLength - 1)].TrimEnd() + ".";
    }

    private static TextChunker CreateChunker(int targetChunkSize = 1000, int overlapSize = 150)
    {
        return new TextChunker(Options.Create(new ChunkingOptions
        {
            TargetChunkSize = targetChunkSize,
            OverlapSize = overlapSize
        }));
    }
}
