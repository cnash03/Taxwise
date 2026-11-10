using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxWise.Rag.Voyage;

namespace TaxWise.Rag.Tests.Voyage;

public sealed class VoyageClientTests
{
    private static VoyageOptions Options(int batch = 128) => new() { ApiKey = "test-key", EmbedBatchSize = batch };

    private static VoyageClient Client(FakeHandler handler, VoyageOptions? options = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.voyageai.com/v1/") }, Microsoft.Extensions.Options.Options.Create(options ?? Options()));

    /// <summary>An embeddings response for the inputs in <paramref name="body"/>, in reverse order to prove we sort by index.</summary>
    private static HttpResponseMessage EmbedResponse(string body)
    {
        var count = JsonNode.Parse(body)!["input"]!.AsArray().Count;
        var data = Enumerable.Range(0, count).Reverse()
            .Select(i => new { @object = "embedding", embedding = new[] { (float)i, 0.5f }, index = i });
        return FakeHandler.Json(JsonSerializer.Serialize(new { @object = "list", data, model = "voyage-3.5", usage = new { total_tokens = 10 } }));
    }

    [Fact]
    public async Task Embed_SendsDocumentedRequestShape()
    {
        var handler = new FakeHandler((_, body) => EmbedResponse(body));

        await Client(handler).EmbedAsync(["room and board"], VoyageInputType.Document);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.voyageai.com/v1/embeddings", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("voyage-3.5", (string?)json["model"]);
        Assert.Equal("document", (string?)json["input_type"]);
        Assert.Equal("room and board", (string?)json["input"]![0]);
    }

    [Fact]
    public async Task Embed_QueryInputType_IsSentAsQuery()
    {
        var handler = new FakeHandler((_, body) => EmbedResponse(body));
        await Client(handler).EmbedAsync(["q"], VoyageInputType.Query);
        Assert.Equal("query", (string?)JsonNode.Parse(handler.Calls[0].Body)!["input_type"]);
    }

    [Fact]
    public async Task Embed_ReturnsVectorsInInputOrder()
    {
        var handler = new FakeHandler((_, body) => EmbedResponse(body));

        var vectors = await Client(handler).EmbedAsync(["a", "b", "c"], VoyageInputType.Document);

        Assert.Equal([0f, 1f, 2f], vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task Embed_SplitsIntoBatchesOf128()
    {
        var handler = new FakeHandler((_, body) => EmbedResponse(body));
        var texts = Enumerable.Range(0, 300).Select(i => $"t{i}").ToList();

        var vectors = await Client(handler).EmbedAsync(texts, VoyageInputType.Document);

        Assert.Equal([128, 128, 44], handler.Calls.Select(c => JsonNode.Parse(c.Body)!["input"]!.AsArray().Count));
        Assert.Equal(300, vectors.Count);
    }

    [Fact]
    public async Task Rerank_SendsDocumentedRequestShape_AndSortsByScore()
    {
        const string response = """
            {"object":"list","data":[{"index":1,"relevance_score":0.31},{"index":0,"relevance_score":0.82}],
             "model":"rerank-2.5","usage":{"total_tokens":42}}
            """;
        var handler = new FakeHandler((_, _) => FakeHandler.Json(response));

        var results = await Client(handler).RerankAsync("is my scholarship taxable", ["p1", "p2"], topK: 4);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.voyageai.com/v1/rerank", request.RequestUri!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("is my scholarship taxable", (string?)json["query"]);
        Assert.Equal("rerank-2.5", (string?)json["model"]);
        Assert.Equal(4, (int?)json["top_k"]);
        Assert.Equal(2, json["documents"]!.AsArray().Count);
        Assert.Equal([new RerankResult(0, 0.82), new RerankResult(1, 0.31)], results);
    }

    [Fact]
    public async Task Rerank_OmitsTopK_WhenNotGiven()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{"data":[]}"""));
        await Client(handler).RerankAsync("q", ["p1"]);
        Assert.Null(JsonNode.Parse(handler.Calls[0].Body)!["top_k"]);
    }

    [Fact]
    public async Task ErrorStatus_ThrowsVoyageApiException()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("""{"detail":"bad model"}""", HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<VoyageApiException>(() => Client(handler).EmbedAsync(["a"], VoyageInputType.Document));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("bad model", ex.Message);
    }

    [Fact]
    public async Task MissingApiKey_FailsWithAHelpfulMessage_WithoutCallingVoyage()
    {
        var handler = new FakeHandler((_, body) => EmbedResponse(body));
        var options = Options();
        options.ApiKey = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler, options).EmbedAsync(["a"], VoyageInputType.Document));

        Assert.Contains("user-secrets", ex.Message);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RegisteredClient_RetriesTransientErrors(HttpStatusCode transient)
    {
        var attempts = 0;
        var handler = new FakeHandler((_, body) => ++attempts < 3 ? FakeHandler.Json("{}", transient) : EmbedResponse(body));
        var services = new ServiceCollection();
        services.Configure<VoyageOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
        });
        services.AddVoyageClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var vectors = await provider.GetRequiredService<IVoyageClient>().EmbedAsync(["a"], VoyageInputType.Document);

        Assert.Single(vectors);
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task RegisteredClient_GivesUpAfterThreeRetries()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}", HttpStatusCode.TooManyRequests));
        var services = new ServiceCollection();
        services.Configure<VoyageOptions>(o =>
        {
            o.ApiKey = "test-key";
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
        });
        services.AddVoyageClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<VoyageApiException>(() =>
            provider.GetRequiredService<IVoyageClient>().EmbedAsync(["a"], VoyageInputType.Document));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(4, handler.Calls.Count); // 1 try + 3 retries
    }
}
