using MattGPT.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace MattGPT.Contracts.Services;

/// <summary>
/// Represents a vector store for conversation embeddings, allowing upsert and similarity search operations.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Inserts a new conversation or updates an existing one in the storage asynchronously, associating it with the
    /// specified vector.
    /// </summary>
    /// <remarks>This method performs an upsert operation: if the specified conversation does not exist in the
    /// storage, it is inserted; otherwise, the existing conversation is updated. Ensure that the conversation object is
    /// properly initialized and the vector is valid before calling this method.</remarks>
    /// <param name="conversation">The conversation to insert or update. Cannot be null.</param>
    /// <param name="vector">An array of floating-point values representing the vector to associate with the conversation. The array must not
    /// be empty.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation. The default value is <see
    /// cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous upsert operation.</returns>
    Task UpsertAsync(StoredConversation conversation, float[] vector, CancellationToken ct = default);

    /// <summary>
    /// Searches for vector-based results that are most similar to the specified query vector.
    /// </summary>
    /// <remarks>An exception is thrown if the input parameters are invalid or if the operation is
    /// canceled.</remarks>
    /// <param name="queryVector">The vector representation of the query to use for similarity search. This array must not be null and should
    /// contain valid floating-point values.</param>
    /// <param name="limit">The maximum number of results to return. Must be a positive integer. The default value is 5.</param>
    /// <param name="userId">An optional identifier for the user making the request. This can be used to provide user-specific search results
    /// or context.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a read-only list of vector search
    /// results that match the query vector.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryVector, int limit = 5, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Asynchronously retrieves the total number of points available in the vector store.
    /// </summary>
    /// <remarks>This method may throw an exception if the operation is canceled or if an error occurs during
    /// the retrieval process.</remarks>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the total number of points as an
    /// unsigned long, or null if the count cannot be determined.</returns>
    Task<ulong?> GetPointCountAsync(CancellationToken ct = default);
}
