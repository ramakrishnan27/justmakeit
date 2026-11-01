namespace Platform.Auth.DTOs;

/// <param name="Role">Always <c>siteadmin</c>; it is what the access token's role claim carries.</param>
public sealed record SiteMeResponse(UserDto User, string Role);
