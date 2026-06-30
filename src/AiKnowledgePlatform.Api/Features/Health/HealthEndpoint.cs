namespace AiKnowledgePlatform.Api.Features.Health;

public static class HealthEndpoint
{
    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => new HealthResponse("Healthy", "AiKnowledgePlatform.Api"))
            .WithName("GetHealth")
            .WithTags("Health")
            .WithSummary("Get API health")
            .WithDescription("Returns the current health status of the API.")
            .Produces<HealthResponse>(StatusCodes.Status200OK);

        return app;
    }
}
