namespace Platform.Auth.DTOs;

/// <param name="Role">Always <c>siteadmin</c>; it is what the access token's role claim carries.</param>
public sealed record SiteMeResponse(UserDto User, string Role)
{
    /// <summary>When the sign-in ends (the token's <c>exp</c>). Filled in by the controller, which can see the token.</summary>
    public DateTimeOffset? SessionExpiresAt { get; init; }
}
