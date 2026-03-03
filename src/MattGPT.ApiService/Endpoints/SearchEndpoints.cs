using Microsoft.Extensions.AI;
using MattGPT.ApiService.Services;

namespace MattGPT.ApiService.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        // Search conversations using semantic similarity.
        app.MapGet("/search", async (
            string q,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IVectorStore vectorStore,
            ICurrentUserService currentUser,
            CancellationToken ct,
            int limit = 5) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest("Query parameter 'q' is required.");

            if (limit is < 1 or > 100) limit = 5;

            var embeddings = await embeddingGenerator.GenerateAsync([q], cancellationToken: ct);
            var queryVector = embeddings[0].Vector.ToArray();

            var results = await vectorStore.SearchAsync(queryVector, limit, currentUser.UserId, ct);

            return Results.Ok(results.Select(r => new
            {
                conversationId = r.ConversationId,
                score = r.Score,
                title = r.Title,
                summary = r.Summary,
            }));
        })
        .WithName("SearchConversations");

        return app;
    }
}
