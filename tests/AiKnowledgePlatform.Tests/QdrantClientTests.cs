using System.Net;
using System.Text;
using System.Text.Json;
using AiKnowledgePlatform.Api.Storage;
using Microsoft.Extensions.Configuration;

namespace AiKnowledgePlatform.Tests;

public sealed class QdrantClientTests
{
    [Fact]
    public async Task SearchAsync_SendsWithPayloadAsSnakeCase()
    {
        string? requestBody = null;
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":[]}""", Encoding.UTF8, "application/json")
            };
        }));
        var client = new QdrantClient(httpClient, CreateConfiguration());

        await client.SearchAsync([0.1f, 0.2f, 0.3f], 5);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        Assert.True(document.RootElement.TryGetProperty("with_payload", out var withPayload));
        Assert.True(withPayload.GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("withPayload", out _));
    }

    [Fact]
    public async Task SearchAsync_IgnoresResultsWithoutPayload()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"result":[{"score":0.9,"payload":null}]}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var client = new QdrantClient(httpClient, CreateConfiguration());

        var results = await client.SearchAsync([0.1f, 0.2f, 0.3f], 5);

        Assert.Empty(results);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Qdrant:BaseUrl"] = "http://localhost:6333",
                ["Qdrant:CollectionName"] = "document_chunks",
                ["Qdrant:VectorSize"] = "768"
            })
            .Build();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
