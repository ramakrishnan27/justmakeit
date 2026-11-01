namespace Platform.Auth.Models;

public sealed record NewSiteRefreshToken(Guid Id, Guid UserId, string TokenHash, DateTimeOffset ExpiresAt);
