using Platform.Auth.DTOs;

namespace Platform.Auth.Models;

/// <summary>
/// What a sign-in produces. The access token goes in the response body; the refresh token goes only
/// in the HttpOnly cookie, which is why it is kept apart from <see cref="TokenResponse"/>.
/// </summary>
public sealed record AuthResult(TokenResponse Tokens, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
