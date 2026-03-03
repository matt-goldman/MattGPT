namespace MattGPT.ApiService.Services;

/// <summary>
/// Provides the current user's identity for data-scoping.
/// When auth is enabled and a user is logged in, <see cref="UserId"/> returns their Identity user ID.
/// When auth is disabled, <see cref="UserId"/> returns <c>null</c>, and repositories
/// filter to untagged (userId == null) data only.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's ID, or <c>null</c> if no user is logged in or auth is disabled.</summary>
    string? UserId { get; }
}
