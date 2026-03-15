using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MattGPT.ApiClient.Models;

namespace MattGPT.ApiClient.Services;

/// <inheritdoc cref="IAuthService"/>
public sealed class AuthService(IHttpClientFactory factory, IAuthFailureHandler authFailureHandler) : IAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private HttpClient CreateClient() => factory.CreateClient(MattGptApiClientDefaults.ClientName);

    /// <inheritdoc/>
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { email, password }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Invalid email or password."
                : "Login failed. Please try again.";
            return new LoginResult(false, ErrorMessage: message);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        if (token?.AccessToken is null)
            return new LoginResult(false, ErrorMessage: "Unexpected response from server.");

        return new LoginResult(true, Token: token);
    }

    /// <inheritdoc/>
    public async Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new { email, password }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new RegisterResult(false, ErrorMessage: $"Registration failed: {body}");
        }

        return new RegisterResult(true);
    }

    /// <inheritdoc/>
    public async Task<UserInfo?> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // HandleAsync is called for its side-effect (e.g., redirect to login or token refresh).
            // A retry is not possible here because the access token is an explicit caller-provided
            // parameter; the callers that supply an updated token are responsible for retrying.
            await authFailureHandler.HandleAsync(cancellationToken);
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<UserInfo>(JsonOptions, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<LoginResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "Invalid refresh token."
                : "Token refresh failed. Please try again.";
            return new LoginResult(false, ErrorMessage: message);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);

        if (token?.AccessToken is null)
            return new LoginResult(false, ErrorMessage: "Unexpected response from server.");

        return new LoginResult(true, Token: token);
    }
}
