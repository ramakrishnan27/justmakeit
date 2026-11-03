namespace Platform.Auth.Models;

/// <summary>
/// What a sign-in produces: the signed access token and when it ends. The controller puts the token in the
/// HttpOnly cookie and answers with the expiry only (see <c>SessionResponse</c>).
/// </summary>
public sealed record AuthResult(string AccessToken, DateTimeOffset ExpiresAt);
