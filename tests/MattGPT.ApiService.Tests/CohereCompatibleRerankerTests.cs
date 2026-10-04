using System.Net;
using System.Text;
using System.Text.Json;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Tests;

public class CohereCompatibleRerankerTests
{
    /// <summary>Captures the request and replies with a fixed status and body.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static CohereCompatibleReranker Create(StubHandler handler, LlmOptions? options = null)
        => new(
            new HttpClient(handler),
            Options.Create(options ?? new LlmOptions { RerankingModelId = "rerank-test" }),
            NullLogger<CohereCompatibleReranker>.Instance);

    private const string ResultsJson = """
        {"id":"x","results":[{"index":1,"relevance_score":0.9},{"index":0,"relevance_score":0.2}],"meta":{}}
        """;

    [Fact]
    public async Task RerankAsync_SendsCohereFormatRequest()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        await Create(handler).RerankAsync("my query", ["doc a", "doc b"], topN: 1);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(CohereCompatibleReranker.DefaultEndpoint, handler.Request.RequestUri!.ToString());

        using var json = JsonDocument.Parse(handler.RequestBody!);
        var root = json.RootElement;
        Assert.Equal("rerank-test", root.GetProperty("model").GetString());
        Assert.Equal("my query", root.GetProperty("query").GetString());
        Assert.Equal(["doc a", "doc b"], root.GetProperty("documents").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, root.GetProperty("top_n").GetInt32());
    }

    [Fact]
    public async Task RerankAsync_UsesConfiguredEndpoint()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        await Create(handler, new LlmOptions
        {
            RerankingModelId = "bge-reranker-v2-m3",
            RerankingEndpoint = "http://localhost:8000/v1/rerank",
        }).RerankAsync("q", ["a", "b"], topN: 2);

        Assert.Equal("http://localhost:8000/v1/rerank", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task RerankAsync_ParsesResultsMostRelevantFirst()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        var results = await Create(handler).RerankAsync("q", ["a", "b"], topN: 2);

        Assert.Equal([1, 0], results.Select(r => r.Index));
        Assert.Equal([0.9f, 0.2f], results.Select(r => r.Score));
    }

    [Fact]
    public async Task RerankAsync_TopNLargerThanDocuments_IsClamped()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        await Create(handler).RerankAsync("q", ["a", "b"], topN: 10);

        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(2, json.RootElement.GetProperty("top_n").GetInt32());
    }

    [Fact]
    public async Task RerankAsync_WithApiKey_SendsBearerToken()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        await Create(handler, new LlmOptions { RerankingModelId = "m", RerankingApiKey = "secret" })
            .RerankAsync("q", ["a", "b"], topN: 2);

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("secret", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task RerankAsync_WithoutRerankingApiKey_SendsNoAuthorization_EvenIfChatKeySet()
    {
        // The chat provider's key must never be sent to the (usually different) rerank service.
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        await Create(handler, new LlmOptions { RerankingModelId = "m", ApiKey = "chat-provider-key" })
            .RerankAsync("q", ["a", "b"], topN: 2);

        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task RerankAsync_ErrorStatus_ThrowsWithServerBody()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"message":"model 'nope' not found"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => Create(handler).RerankAsync("q", ["a"], topN: 1));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("model 'nope' not found", ex.Message);
    }

    [Fact]
    public async Task RerankAsync_IgnoresOutOfRangeAndDuplicateIndices()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"results":[{"index":5,"relevance_score":0.99},{"index":0,"relevance_score":0.8},{"index":0,"relevance_score":0.7}]}
            """);

        var results = await Create(handler).RerankAsync("q", ["a", "b"], topN: 2);

        var only = Assert.Single(results);
        Assert.Equal(0, only.Index);
        Assert.Equal(0.8f, only.Score);
    }

    [Fact]
    public async Task RerankAsync_NoDocuments_DoesNotCallService()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ResultsJson);

        var results = await Create(handler).RerankAsync("q", [], topN: 5);

        Assert.Empty(results);
        Assert.Equal(0, handler.Calls);
    }
}
