using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MattGPT.Contracts;
using MattGPT.Contracts.Models;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MattGPT.WeaviateModule.Services;

/// <summary>
/// Weaviate-backed implementation of <see cref="IVectorStore"/>.
/// Uses the Weaviate REST API v1 directly via <see cref="HttpClient"/> for reliability,
/// as the .NET client ecosystem is immature.
/// </summary>
/// <remarks>
/// The Weaviate class name is derived from <see cref="VectorStoreOptions.IndexName"/>
/// (first letter uppercased to satisfy Weaviate's naming convention). The class is
/// auto-created on first upsert if it doesn't exist.
/// </remarks>
public class WeaviateVectorStore(
    HttpClient httpClient,
    IOptions<VectorStoreOptions> options,
    ILogger<WeaviateVectorStore> logger) : IVectorStore
{
    private volatile bool _classEnsured;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    // Weaviate class names must start with an uppercase letter.
    private readonly string ClassName = CapitalizeFirst(options.Value.IndexName);

    /// <inheritdoc/>
    public async Task UpsertAsync(
        StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0)
        {
            await DeleteAsync(conversation.ConversationId, ct);
            return;
        }

        await EnsureClassAsync(chunks[0].Vector.Length, ct);

        // Replace, not merge: a conversation re-embedded into fewer chunks (or under another strategy)
        // must not leave the previous objects behind to be matched against.
        await DeleteAsync(conversation.ConversationId, ct);

        foreach (var chunk in chunks)
        {
            var id = ChunkPointId.Uuid(conversation.ConversationId, chunk.Chunk.Ordinal).ToString("D");

            var payload = new
            {
                @class = ClassName,
                id,
                properties = new Dictionary<string, object?>
                {
                    ["conversation_id"] = conversation.ConversationId,
                    ["title"] = conversation.Title ?? string.Empty,
                    ["summary"] = conversation.Summary ?? string.Empty,
                    ["create_time"] = conversation.CreateTime ?? 0.0,
                    ["update_time"] = conversation.UpdateTime ?? 0.0,
                    ["default_model_slug"] = conversation.DefaultModelSlug ?? string.Empty,
                    ["gizmo_id"] = conversation.GizmoId ?? string.Empty,
                    ["is_archived"] = conversation.IsArchived ?? false,
                    ["user_id"] = conversation.UserId ?? string.Empty,
                    ["chunk_ordinal"] = chunk.Chunk.Ordinal,
                    ["chunk_count"] = chunks.Count,
                    ["chunk_start_message"] = chunk.Chunk.StartMessageIndex,
                    ["chunk_end_message"] = chunk.Chunk.EndMessageIndex,
                    ["chunk_strategy"] = chunk.Chunk.Strategy.ToString(),
                },
                vector = chunk.Vector
            };

            // Try PUT (update), fall back to POST (create) if 404.
            var putResponse = await httpClient.PutAsJsonAsync($"v1/objects/{ClassName}/{id}", payload, ct);
            if (putResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var postResponse = await httpClient.PostAsJsonAsync("v1/objects", payload, ct);
                postResponse.EnsureSuccessStatusCode();
            }
            else
            {
                putResponse.EnsureSuccessStatusCode();
            }
        }

        logger.LogDebug(
            "Upserted conversation {Id} ({Title}) to Weaviate as {Chunks} chunk(s).",
            conversation.ConversationId, conversation.Title, chunks.Count);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        // Batch delete by filter: the object ids are derivable from the ordinals, but only if the
        // previous chunk count is known, and it is not.
        var request = new HttpRequestMessage(HttpMethod.Delete, "v1/batch/objects")
        {
            Content = JsonContent.Create(new
            {
                match = new
                {
                    @class = ClassName,
                    where = new
                    {
                        path = new[] { "conversation_id" },
                        @operator = "Equal",
                        valueText = conversationId,
                    }
                }
            })
        };

        var response = await httpClient.SendAsync(request, ct);

        // 404 means the class does not exist yet, which is not an error for a delete.
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default)
    {
        // The query asks for the chunk properties by name, and a class created before chunking does not
        // have them, which fails the whole GraphQL query rather than returning them empty.
        await EnsureClassAsync(queryVector.Length, ct);

        var vectorStr = string.Join(", ", queryVector.Select(v => v.ToString("G", CultureInfo.InvariantCulture)));

        // Build the optional user_id filter. Escape backslashes and double-quotes to prevent injection.
        var whereClause = userId is not null
            ? $"where: {{ path: [\"user_id\"], operator: Equal, valueText: \"{EscapeGraphQLString(userId)}\" }}"
            : string.Empty;

        var graphql = new
        {
            query = $$"""
            {
              Get {
                {{ClassName}}(
                  limit: {{limit}}
                  nearVector: { vector: [{{vectorStr}}] }
                  {{whereClause}}
                ) {
                  conversation_id
                  title
                  summary
                  chunk_ordinal
                  chunk_count
                  chunk_start_message
                  chunk_end_message
                  chunk_strategy
                  _additional { id distance }
                }
              }
            }
            """
        };

        var response = await httpClient.PostAsJsonAsync("v1/graphql", graphql, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var results = new List<VectorSearchResult>();
        if (json.TryGetProperty("data", out var data) &&
            data.TryGetProperty("Get", out var get) &&
            get.TryGetProperty(ClassName, out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var conversationId = item.GetProperty("conversation_id").GetString() ?? string.Empty;
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                var summary = item.TryGetProperty("summary", out var s) ? s.GetString() : null;

                // Weaviate returns distance (lower = more similar); convert to score (higher = more similar).
                float score = 0f;
                if (item.TryGetProperty("_additional", out var additional) &&
                    additional.TryGetProperty("distance", out var dist))
                {
                    score = 1f - dist.GetSingle();
                }

                results.Add(new VectorSearchResult(conversationId, score, title, summary, ReadChunk(item)));
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<ulong?> GetPointCountAsync(CancellationToken ct = default)
    {
        try
        {
            var graphql = new
            {
                query = $$"""
                {
                  Aggregate {
                    {{ClassName}} {
                      meta { count }
                    }
                  }
                }
                """
            };

            var response = await httpClient.PostAsJsonAsync("v1/graphql", graphql, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("data", out var data) &&
                data.TryGetProperty("Aggregate", out var agg) &&
                agg.TryGetProperty(ClassName, out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("meta", out var meta) &&
                        meta.TryGetProperty("count", out var count))
                    {
                        return (ulong)count.GetInt64();
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get Weaviate object count.");
            return null;
        }
    }

    /// <summary>
    /// Ensures the Weaviate class exists with the correct properties.
    /// Called lazily on the first upsert.
    /// </summary>
    private async Task EnsureClassAsync(int dimensions, CancellationToken ct)
    {
        if (_classEnsured) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_classEnsured) return;

            // Check if class already exists.
            var existsResponse = await httpClient.GetAsync($"v1/schema/{ClassName}", ct);
            if (existsResponse.IsSuccessStatusCode)
            {
                // Ensure the properties added after the class was first created exist on pre-existing
                // classes (idempotent). A missing chunk property would make every hit look like a
                // pre-chunking vector.
                foreach (var (name, dataType) in new[]
                {
                    ("user_id", "text"),
                    ("chunk_ordinal", "int"),
                    ("chunk_count", "int"),
                    ("chunk_start_message", "int"),
                    ("chunk_end_message", "int"),
                    ("chunk_strategy", "text"),
                })
                {
                    var addPropResponse = await httpClient.PostAsJsonAsync(
                        $"v1/schema/{ClassName}/properties",
                        new { name, dataType = new[] { dataType } },
                        ct);
                    // 200 = added, 422 = already exists — both are acceptable.
                    if (!addPropResponse.IsSuccessStatusCode && (int)addPropResponse.StatusCode != 422)
                    {
                        logger.LogWarning(
                            "Could not ensure {Property} property on Weaviate class '{Class}': {Status}",
                            name, ClassName, addPropResponse.StatusCode);
                    }
                }

                _classEnsured = true;
                return;
            }

            var classDefinition = new
            {
                @class = ClassName,
                description = "Conversation embeddings for MattGPT RAG pipeline",
                vectorizer = "none",
                properties = new object[]
                {
                    new { name = "conversation_id", dataType = new[] { "text" } },
                    new { name = "title", dataType = new[] { "text" } },
                    new { name = "summary", dataType = new[] { "text" } },
                    new { name = "create_time", dataType = new[] { "number" } },
                    new { name = "update_time", dataType = new[] { "number" } },
                    new { name = "default_model_slug", dataType = new[] { "text" } },
                    new { name = "gizmo_id", dataType = new[] { "text" } },
                    new { name = "is_archived", dataType = new[] { "boolean" } },
                    new { name = "user_id", dataType = new[] { "text" } },
                    new { name = "chunk_ordinal", dataType = new[] { "int" } },
                    new { name = "chunk_count", dataType = new[] { "int" } },
                    new { name = "chunk_start_message", dataType = new[] { "int" } },
                    new { name = "chunk_end_message", dataType = new[] { "int" } },
                    new { name = "chunk_strategy", dataType = new[] { "text" } },
                }
            };

            var createResponse = await httpClient.PostAsJsonAsync("v1/schema", classDefinition, ct);
            createResponse.EnsureSuccessStatusCode();

            logger.LogInformation(
                "Created Weaviate class '{Class}' with {Dims} dimensions.",
                ClassName, dimensions);

            _classEnsured = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// The chunk address stored on an object, or null for an object written before chunking existed
    /// (which is treated as the whole conversation).
    /// </summary>
    private static ChunkHit? ReadChunk(JsonElement item)
    {
        if (ReadInt(item, "chunk_ordinal") is not { } ordinal)
            return null;

        var strategyName = item.TryGetProperty("chunk_strategy", out var s) ? s.GetString() : null;
        var strategy = Enum.TryParse<ChunkingStrategy>(strategyName, out var parsed)
            ? parsed
            : ChunkingStrategy.WholeConversation;

        return new ChunkHit(
            Ordinal:           ordinal,
            ChunkCount:        ReadInt(item, "chunk_count") ?? 1,
            StartMessageIndex: ReadInt(item, "chunk_start_message") ?? 0,
            EndMessageIndex:   ReadInt(item, "chunk_end_message") ?? -1,
            Strategy:          strategy);
    }

    private static int? ReadInt(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    /// <summary>Returns the input string with its first character uppercased.</summary>
    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>
    /// Escapes a string for safe interpolation into a GraphQL string literal
    /// by escaping backslashes and double-quote characters.
    /// </summary>
    private static string EscapeGraphQLString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
