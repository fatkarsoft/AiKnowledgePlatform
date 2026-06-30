namespace AiKnowledgePlatform.Api.Features.Search;

public sealed record SearchCandidate(
    string DocumentId,
    int ChunkIndex,
    string Text,
    string FileName,
    double? SemanticScore,
    bool LexicalMatched)
{
    public double Score => SemanticScore.GetValueOrDefault() + (LexicalMatched ? 1 : 0);

    public SearchResult ToSearchResult()
    {
        return new SearchResult(DocumentId, ChunkIndex, Score, Text, FileName);
    }
}
