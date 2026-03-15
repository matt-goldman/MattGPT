namespace MattGPT.ApiClient.Services;

/// <summary>
/// No-op implementation used when authentication is disabled.
/// </summary>
public sealed class NoOpAuthFailureHandler : IAuthFailureHandler
{
    public Task<bool> HandleAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
