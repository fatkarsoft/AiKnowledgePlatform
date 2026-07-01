namespace AiKnowledgePlatform.Api.Features.Search;

public sealed record SearchCandidate(
    string DocumentId,
    int ChunkIndex,
    string Text,
    string FileName,
    double? SemanticScore,
    double? LexicalScore,
    bool LexicalMatched)
{
    public double Score => SemanticScore.GetValueOrDefault() + LexicalScore.GetValueOrDefault();

    public SearchResult ToSearchResult()
    {
        return new SearchResult(DocumentId, ChunkIndex, Score, Text, FileName);
    }
}
