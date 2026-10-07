using MattGPT.Contracts.Models;

namespace MattGPT.Contracts.Services;

/// <summary>
/// Represents a vector store for conversation chunk embeddings, allowing upsert and similarity search
/// operations.
/// </summary>
/// <remarks>
/// A conversation is stored as one or more chunks (<see cref="ConversationChunk"/>), each with its own
/// vector. Search returns chunk-level hits; pooling them to the conversation is the caller's job
/// (ADR-016). Score semantics differ by implementation — Weaviate converts a distance, Qdrant returns
/// raw cosine, Azure AI Search and Pinecone return their own scores — so scores are comparable within
/// one configured store and not across stores.
/// </remarks>
public interface IVectorStore
{
    /// <summary>
    /// Replaces the stored vectors for a conversation with <paramref name="chunks"/>.
    /// </summary>
    /// <remarks>
    /// This is a replace, not a merge: every vector previously stored for the conversation is removed
    /// first, so a conversation re-embedded under a different chunking strategy (or into a different
    /// number of chunks) cannot leave stale chunks behind to be matched against.
    /// </remarks>
    /// <param name="conversation">The conversation the chunks belong to. Cannot be null.</param>
    /// <param name="chunks">The conversation's chunks and their vectors. An empty list just clears the conversation.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    Task UpsertAsync(StoredConversation conversation, IReadOnlyList<ChunkVector> chunks, CancellationToken ct = default);

    /// <summary>
    /// Removes every vector stored for a conversation. A conversation with no stored vectors is not
    /// an error.
    /// </summary>
    Task DeleteAsync(string conversationId, CancellationToken ct = default);

    /// <summary>
    /// Searches for the chunk vectors most similar to the specified query vector.
    /// </summary>
    /// <remarks>
    /// <paramref name="limit"/> counts <em>chunks</em>, not conversations: several hits may belong to
    /// the same conversation, and a caller that wants a number of conversations must ask for enough
    /// chunks to yield them. An exception is thrown if the input parameters are invalid or if the
    /// operation is canceled.
    /// </remarks>
    /// <param name="queryVector">The vector representation of the query to use for similarity search. This array must not be null and should
    /// contain valid floating-point values.</param>
    /// <param name="limit">The maximum number of chunk hits to return. Must be a positive integer. The default value is 5.</param>
    /// <param name="userId">An optional identifier for the user making the request. This can be used to provide user-specific search results
    /// or context.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a read-only list of vector search
    /// results that match the query vector, most similar first.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously retrieves the total number of points (chunk vectors, not conversations)
    /// available in the vector store.
    /// </summary>
    /// <remarks>This method may throw an exception if the operation is canceled or if an error occurs during
    /// the retrieval process.</remarks>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the total number of points as an
    /// unsigned long, or null if the count cannot be determined.</returns>
    Task<ulong?> GetPointCountAsync(CancellationToken ct = default);
}
