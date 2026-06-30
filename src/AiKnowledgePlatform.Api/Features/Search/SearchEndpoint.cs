namespace AiKnowledgePlatform.Api.Features.Search;

public static class SearchEndpoint
{
    public static IEndpointRouteBuilder MapSearchEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/search", async (
            SearchRequest request,
            SemanticSearchService searchService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest("Query is required.");
            }

            var topK = request.TopK.GetValueOrDefault(5);
            if (topK <= 0)
            {
                return Results.BadRequest("TopK must be greater than zero.");
            }

            var results = await searchService.SearchAsync(request.Query, topK);

            return Results.Ok(results);
        })
        .WithName("Search")
        .WithTags("Search")
        .WithSummary("Semantic search")
        .WithDescription("Generates a local query embedding and searches indexed document chunks in Qdrant.")
        .Produces<IReadOnlyList<SearchResult>>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest);

        return app;
    }
}
