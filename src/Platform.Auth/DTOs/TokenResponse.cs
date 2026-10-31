namespace Platform.Auth.DTOs;

/// <summary>
/// The access token. The refresh token is deliberately not here: it travels only in an HttpOnly
/// cookie scoped to /v1/auth, so scripts on the page can never read it.
/// </summary>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn)
{
    public const string BearerType = "Bearer";
}
