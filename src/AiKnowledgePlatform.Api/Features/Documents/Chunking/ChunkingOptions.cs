namespace AiKnowledgePlatform.Api.Features.Documents.Chunking;

public sealed class ChunkingOptions
{
    public const string SectionName = "Chunking";

    public int TargetChunkSize { get; set; } = 500;

    public int OverlapSize { get; set; } = 100;
}
