using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace MattGPT.ApiService.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/llm/status", async (IChatClient chatClient, IOptions<LlmOptions> options) =>
        {
            var opts = options.Value;
            bool reachable;
            string? error = null;

            try
            {
                // Send a minimal prompt with a short timeout to test reachability.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await chatClient.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, cts.Token);
                reachable = response is not null;
            }
            catch (Exception ex)
            {
                reachable = false;
                error = ex.Message;
            }

            return Results.Ok(new
            {
                provider = opts.Provider,
                modelId = opts.ModelId,
                endpoint = opts.Endpoint,
                reachable,
                error
            });
        })
        .WithName("GetLlmStatus");

        // RAG pipeline health / diagnostics endpoint.
        app.MapGet("/rag/diagnostics", async (
            IConversationRepository repository,
            IVectorStore vectorStore,
            IOptions<RagOptions> ragOptions,
            IOptions<LlmOptions> llmOptions,
            CancellationToken ct) =>
        {
            var opts = ragOptions.Value;
            var llm = llmOptions.Value;

            // 1. MongoDB conversation counts by processing status.
            var statusCounts = await repository.GetStatusCountsAsync(ct: ct);

            // 2. Qdrant point count.
            ulong? qdrantPoints = null;
            string? qdrantError = null;
            try
            {
                qdrantPoints = await vectorStore.GetPointCountAsync(ct);
            }
            catch (Exception ex)
            {
                qdrantError = ex.Message;
            }

            // 3. Derive pipeline status diagnostics.
            var totalConversations = statusCounts.Values.Sum();
            var embedded = statusCounts.GetValueOrDefault(ConversationProcessingStatus.Embedded);
            var summarised = statusCounts.GetValueOrDefault(ConversationProcessingStatus.Summarised);
            var imported = statusCounts.GetValueOrDefault(ConversationProcessingStatus.Imported);
            var summaryErrors = statusCounts.GetValueOrDefault(ConversationProcessingStatus.SummaryError);
            var embeddingErrors = statusCounts.GetValueOrDefault(ConversationProcessingStatus.EmbeddingError);

            var issues = new List<string>();
            if (totalConversations == 0)
                issues.Add("No conversations in MongoDB. Upload a ChatGPT export via POST /conversations/upload.");
            else if (imported > 0 || summarised > 0)
            {
                var unembedded = imported + summarised;
                issues.Add($"{unembedded} conversations are not yet embedded. Run POST /conversations/embed (or re-upload — embedding is automatic on import).");
            }
            if (summaryErrors > 0)
                issues.Add($"{summaryErrors} conversations failed summarisation (non-blocking — embedding uses raw content).");
            if (embeddingErrors > 0)
                issues.Add($"{embeddingErrors} conversations failed embedding generation.");
            if (qdrantPoints is null)
                issues.Add("Qdrant collection does not exist yet. Run POST /conversations/embed to create it.");
            else if (qdrantPoints == 0)
                issues.Add("Qdrant collection exists but is empty.");
            if (embedded > 0 && qdrantPoints is not null && (long)qdrantPoints.Value < embedded)
                issues.Add($"Qdrant has {qdrantPoints} points but MongoDB has {embedded} embedded conversations — mismatch.");

            var healthy = issues.Count == 0;

            return Results.Ok(new
            {
                healthy,
                issues,
                ragConfig = new { mode = opts.Mode.ToString(), topK = opts.TopK, minScore = opts.MinScore, autoTopK = opts.AutoTopK, autoMinScore = opts.AutoMinScore, toolMaxResults = opts.ToolMaxResults },
                llmConfig = new { provider = llm.Provider, modelId = llm.ModelId, embeddingModelId = llm.EmbeddingModelId },
                mongodb = new
                {
                    totalConversations,
                    byStatus = statusCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                },
                qdrant = new
                {
                    pointCount = qdrantPoints,
                    collectionExists = qdrantPoints.HasValue,
                    error = qdrantError,
                },
            });
        })
        .WithName("RagDiagnostics");

        return app;
    }
}
