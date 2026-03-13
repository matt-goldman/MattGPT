namespace MattGPT.ApiClient.Services;

public interface IAuthFailureHandler
{
    /// <summary>
    /// Handle an authentication failure. Returns true if the caller should retry the request
    /// (e.g., token was silently refreshed), false if recovery is underway (e.g., redirect to login).
    /// </summary>
    Task<bool> HandleAsync(CancellationToken cancellationToken = default);
}