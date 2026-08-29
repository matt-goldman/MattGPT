using Microsoft.Extensions.AI;
using MattGPT.ApiService.Services;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Endpoints;

public static class SearchEndpoints
{
    /// <summary>Value of the <c>mode</c> query parameter that selects keyword search.</summary>
    private const string KeywordMode = "keyword";

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        // Search conversations, either semantically (default) or by keyword.
        //
        // The two modes are deliberately not blended: they answer different questions
        // ("what did I discuss about X" vs "where did I mention exactly X"), and the UI
        // exposes the choice as a toggle rather than guessing which the user meant.
        app.MapGet("/search", async (
            string q,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IVectorStore vectorStore,
            IConversationRepository repository,
            ICurrentUserService currentUser,
            CancellationToken ct,
            int limit = 5,
            string? mode = null) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest("Query parameter 'q' is required.");

            if (limit is < 1 or > 100) limit = 5;

            if (string.Equals(mode, KeywordMode, StringComparison.OrdinalIgnoreCase))
                return await KeywordSearchAsync(q, limit, repository, currentUser, ct);

            return await SemanticSearchAsync(q, limit, embeddingGenerator, vectorStore, currentUser, ct);
        })
        .WithName("SearchConversations");

        return app;
    }

    /// <summary>Vector similarity search: matches conversations by meaning.</summary>
    private static async Task<IResult> SemanticSearchAsync(
        string q,
        int limit,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IVectorStore vectorStore,
        ICurrentUserService currentUser,
        CancellationToken ct)
    {
        var embeddings = await embeddingGenerator.GenerateAsync([q], cancellationToken: ct);
        var queryVector = embeddings[0].Vector.ToArray();

        var results = await vectorStore.SearchAsync(queryVector, limit, currentUser.UserId, ct);

        return Results.Ok(results.Select(r => new SearchResponseItem(
            r.ConversationId,
            r.Score,
            r.Title,
            r.Summary,
            [])));
    }

    /// <summary>Database full-text search: matches conversations by the literal words typed.</summary>
    /// <remarks>
    /// Scores are rescaled to be relative to the best hit in the set — see
    /// <see cref="ConversationTextSearchRanking"/> for why the raw backend rank can't be
    /// sent as-is. Clients should present keyword scores as an ordering, not a percentage.
    /// </remarks>
    private static async Task<IResult> KeywordSearchAsync(
        string q,
        int limit,
        IConversationRepository repository,
        ICurrentUserService currentUser,
        CancellationToken ct)
    {
        var results = await repository.SearchTextAsync(q, limit, currentUser.UserId, ct);
        var maxScore = ConversationTextSearchRanking.MaxScore(results);

        return Results.Ok(results.Select(r => new SearchResponseItem(
            r.ConversationId,
            ConversationTextSearchRanking.Relative(r.Score, maxScore),
            r.Title,
            r.Summary,
            r.Snippets)));
    }

    /// <summary>Wire format for a single search hit, shared by both modes.</summary>
    private record SearchResponseItem(
        string ConversationId,
        float Score,
        string? Title,
        string? Summary,
        IReadOnlyList<string> Snippets);
}
