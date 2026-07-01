using System.Net;
using System.Text;
using System.Text.Json;
using AiKnowledgePlatform.Api.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task SearchByTextAsync_LogsDiagnosticDetails()
    {
        var logger = new TestLogger<QdrantClient>();
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":{"points":[]}}""", Encoding.UTF8, "application/json")
            }));
        var client = new QdrantClient(httpClient, CreateConfiguration(), logger);

        await client.SearchByTextAsync("AOF", 5);

        Assert.Contains(logger.Messages, message => message.Contains("Qdrant lexical search term: AOF"));
        Assert.Contains(logger.Messages, message => message.Contains("Qdrant lexical search request:"));
        Assert.Contains(logger.Messages, message => message.Contains("Qdrant lexical search HTTP status: 200"));
        Assert.Contains(logger.Messages, message => message.Contains("Qdrant lexical search raw response body:"));
    }

    [Fact]
    public async Task SearchByTextDebugAsync_ReturnsRawRequestAndResponse()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":{"points":[]}}""", Encoding.UTF8, "application/json")
            }));
        var client = new QdrantClient(httpClient, CreateConfiguration());

        var result = await client.SearchByTextDebugAsync("AOF", 5);

        Assert.Equal(200, result.StatusCode);
        Assert.Contains("\"points\"", result.RawResponseBody);

        var requestJson = JsonSerializer.Serialize(result.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"text\":\"AOF\"", requestJson);
        Assert.Contains("\"with_payload\":true", requestJson);
    }

    [Fact]
    public async Task ScrollDebugAsync_SendsUnfilteredPayloadScroll()
    {
        string? requestBody = null;
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":{"points":[]}}""", Encoding.UTF8, "application/json")
            };
        }));
        var client = new QdrantClient(httpClient, CreateConfiguration());

        await client.ScrollDebugAsync(5);

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody);
        Assert.False(document.RootElement.TryGetProperty("filter", out _));
        Assert.Equal(5, document.RootElement.GetProperty("limit").GetInt32());
        Assert.True(document.RootElement.GetProperty("with_payload").GetBoolean());
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

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel == LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
