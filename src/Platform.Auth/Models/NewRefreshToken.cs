namespace Platform.Auth.Models;

public sealed record NewRefreshToken(Guid Id, Guid AgencyId, Guid UserId, string TokenHash, DateTimeOffset ExpiresAt);
