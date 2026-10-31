using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds a token by the SHA-256 hash of its value through auth_find_refresh_token(), because the
    /// caller does not yet know whose token it is.
    /// </summary>
    Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task InsertAsync(NewRefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes <paramref name="oldTokenId"/> and stores its replacement in one transaction. Returns
    /// false, storing nothing, when the old token was already revoked, which means two requests
    /// presented it at the same time.
    /// </summary>
    Task<bool> RotateAsync(Guid oldTokenId, NewRefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAsync(Guid agencyId, Guid userId, Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes every live token of the user for the agency. Returns how many were revoked.</summary>
    Task<int> RevokeAllAsync(Guid agencyId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
