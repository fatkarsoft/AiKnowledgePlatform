using AiKnowledgePlatform.Api.AI;
using AiKnowledgePlatform.Api.AI.Reranking;
using AiKnowledgePlatform.Api.Features.Documents.Chunking;
using AiKnowledgePlatform.Api.Features.Documents.Embeddings;
using AiKnowledgePlatform.Api.Features.Search;
using AiKnowledgePlatform.Api.Storage;

namespace AiKnowledgePlatform.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.Configure<ChunkingOptions>(configuration.GetSection(ChunkingOptions.SectionName));
        services.Configure<OllamaChatOptions>(configuration.GetSection(OllamaChatOptions.SectionName));
        services.Configure<RerankingOptions>(configuration.GetSection(RerankingOptions.SectionName));
        services.Configure<RerankerServiceOptions>(configuration.GetSection(RerankerServiceOptions.SectionName));
        services.Configure<HybridSearchOptions>(configuration.GetSection(HybridSearchOptions.SectionName));
        services.AddSingleton<TextChunker>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<KeywordOverlapReranker>();
        services.AddScoped<SemanticSearchService>();
        services.AddScoped<LexicalSearchService>();
        services.AddScoped<HybridSearchService>();
        services.AddHttpClient<OllamaEmbeddingGenerator>();
        services.AddHttpClient<OllamaChatClient>();
        services.AddHttpClient<HttpReranker>();
        services.AddScoped<IReranker>(sp => sp.GetRequiredService<HttpReranker>());
        services.AddHttpClient<QdrantClient>();

        return services;
    }
}
