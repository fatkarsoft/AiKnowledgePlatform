using AiKnowledgePlatform.Api.Features.Chat;
using AiKnowledgePlatform.Api.Features.Documents;
using AiKnowledgePlatform.Api.Features.Health;
using AiKnowledgePlatform.Api.Features.Search;

namespace AiKnowledgePlatform.Api.Extensions;

public static class EndpointExtensions
{
    public static WebApplication MapEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "AiKnowledgePlatform API v1");
            });
        }

        app.UseHttpsRedirection();

        app.MapDocumentsEndpoint();
        app.MapHealthEndpoint();
        app.MapSearchEndpoint();
        app.MapChatEndpoint();

        return app;
    }
}
