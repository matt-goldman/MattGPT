using MattGPT.Contracts.Models;

namespace MattGPT.Contracts.Services;

/// <summary>Stores and retrieves the user profile document.</summary>
public interface IUserProfileRepository
{
    /// <summary>Returns the current user profile, or null if none has been stored.</summary>
    Task<UserProfile?> GetAsync(CancellationToken ct = default);

    /// <summary>Upserts the user profile document.</summary>
    Task UpsertAsync(UserProfile profile, CancellationToken ct = default);
}
