namespace MattGPT.ApiClient.Models;

/// <summary>Token response from the Identity API login endpoint.</summary>
public record TokenResponse(string? TokenType, string? AccessToken, int ExpiresIn, string? RefreshToken);

/// <summary>User information returned by the <c>/auth/me</c> endpoint.</summary>
public record UserInfo(string? Id, string? Email);

/// <summary>Result of a login attempt.</summary>
public record LoginResult(bool Success, TokenResponse? Token = null, string? ErrorMessage = null);

/// <summary>Result of a registration attempt.</summary>
public record RegisterResult(bool Success, string? ErrorMessage = null);
