using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface ISiteAdminRefreshTokenRepository
{
    /// <summary>Finds a token by the SHA-256 hash of its value, through auth_find_site_refresh_token().</summary>
    Task<SiteRefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task InsertAsync(NewSiteRefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes <paramref name="oldTokenId"/> and stores its replacement in one transaction. Returns false,
    /// storing nothing, when the old token was already revoked.
    /// </summary>
    Task<bool> RotateAsync(Guid oldTokenId, NewSiteRefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAsync(Guid userId, Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes every live site token of the user. Returns how many were revoked.</summary>
    Task<int> RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
