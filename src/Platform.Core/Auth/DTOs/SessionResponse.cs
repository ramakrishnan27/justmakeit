namespace Platform.Auth.DTOs;

/// <summary>
/// What sign-in answers with. The access token is deliberately not here: it travels only in an HttpOnly cookie, so
/// scripts on the page can never read it. The page learns who is signed in from <c>/me</c>.
/// </summary>
/// <param name="ExpiresAt">When the sign-in ends and the person has to sign in again.</param>
public sealed record SessionResponse(DateTimeOffset ExpiresAt);
