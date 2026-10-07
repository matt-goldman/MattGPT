using System.Security.Cryptography;
using System.Text;

namespace MattGPT.Contracts.Models;

/// <summary>
/// Builds the identifier a vector store stores one chunk under, from the chunk's address.
/// </summary>
/// <remarks>
/// Shared by the store implementations so they agree on the shape: ids are derived from the
/// conversation id and the chunk ordinal, so the same chunk always lands on the same point and a
/// re-embed overwrites rather than duplicating. Stores that require a UUID key use
/// <see cref="Uuid"/>; stores that accept free-form keys use <see cref="Key"/>.
/// </remarks>
public static class ChunkPointId
{
    /// <summary>
    /// A readable, store-safe key for a chunk: the conversation id and ordinal joined by an
    /// underscore, with any character outside <c>[A-Za-z0-9_-]</c> replaced (Azure AI Search restricts
    /// document keys to that set).
    /// </summary>
    public static string Key(string conversationId, int ordinal)
    {
        var sb = new StringBuilder(conversationId.Length + 8);
        foreach (var ch in conversationId)
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');

        return sb.Append('_').Append(ordinal).ToString();
    }

    /// <summary>
    /// A deterministic UUID for a chunk, for stores whose point id must be a UUID. Derived from the
    /// SHA-256 of the conversation id and ordinal, so distinct chunks never collide and the mapping is
    /// stable across runs.
    /// </summary>
    public static Guid Uuid(string conversationId, int ordinal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{conversationId}#{ordinal}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
