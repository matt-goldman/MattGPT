using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Services;

/// <summary>
/// <see cref="IReranker"/> for any service that accepts the Cohere-style rerank request:
/// <c>POST {model, query, documents, top_n}</c> returning <c>{results: [{index, relevance_score}]}</c>.
/// Besides Cohere itself this covers Jina, vLLM, llama.cpp server, Infinity, LiteLLM and Cohere on
/// Azure AI Foundry. Configured from <see cref="LlmOptions.RerankingEndpoint"/>,
/// <see cref="LlmOptions.RerankingModelId"/> and <see cref="LlmOptions.RerankingApiKey"/>.
/// </summary>
public class CohereCompatibleReranker(
    HttpClient httpClient,
    IOptions<LlmOptions> llmOptions,
    ILogger<CohereCompatibleReranker> logger) : IReranker
{
    /// <summary>The value of <see cref="LlmOptions.RerankingProvider"/> this reranker serves (also the default).</summary>
    public const string ProviderName = "Cohere";

    /// <summary>Cohere's hosted rerank endpoint, used when <see cref="LlmOptions.RerankingEndpoint"/> is not set.</summary>
    public const string DefaultEndpoint = "https://api.cohere.com/v2/rerank";

    /// <summary>Maximum characters of a server error body to include in an exception message.</summary>
    private const int MaxErrorBodyChars = 2_000;

    private readonly Uri _endpoint = new(llmOptions.Value.RerankingEndpoint ?? DefaultEndpoint);
    private readonly string _modelId = llmOptions.Value.RerankingModelId
        ?? throw new InvalidOperationException($"{LlmOptions.SectionName}:{nameof(LlmOptions.RerankingModelId)} is required for reranking.");
    private readonly string? _apiKey = llmOptions.Value.RerankingApiKey;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query, IReadOnlyList<string> documents, int topN, CancellationToken ct = default)
    {
        if (documents.Count == 0 || topN <= 0)
            return [];

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new RerankRequest(_modelId, query, documents, Math.Min(topN, documents.Count))),
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // The reason (unknown model, input too long, bad key) is in the body, not the status.
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Length > MaxErrorBodyChars)
                body = string.Concat(body.AsSpan(0, MaxErrorBodyChars), "…(truncated)");

            throw new HttpRequestException(
                $"Reranking request to {_endpoint} (model {_modelId}) failed with {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}: {body}",
                inner: null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<RerankResponse>(ct);
        var results = payload?.Results ?? [];

        // Ignore indices outside the documents sent: they can't be mapped back to a candidate.
        var valid = results
            .Where(r => r.Index >= 0 && r.Index < documents.Count)
            .OrderByDescending(r => r.RelevanceScore)
            .DistinctBy(r => r.Index)
            .Take(topN)
            .Select(r => new RerankResult(r.Index, r.RelevanceScore))
            .ToList();

        var outOfRange = results.Count(r => r.Index < 0 || r.Index >= documents.Count);
        if (outOfRange > 0)
            logger.LogWarning(
                "Reranker returned {Invalid} result(s) with an index outside the {Count} documents sent; ignored.",
                outOfRange, documents.Count);

        logger.LogDebug("Reranked {Count} documents; returned {Returned}.", documents.Count, valid.Count);

        return valid;
    }

    private sealed record RerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents,
        [property: JsonPropertyName("top_n")] int TopN);

    private sealed record RerankResponse(
        [property: JsonPropertyName("results")] List<RerankResponseItem>? Results);

    private sealed record RerankResponseItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] float RelevanceScore);
}
