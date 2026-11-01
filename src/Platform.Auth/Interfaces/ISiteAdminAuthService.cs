using Platform.Auth.DTOs;
using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface ISiteAdminAuthService
{
    /// <summary>
    /// Signs a site admin in. Anyone who is not one, or has the wrong password, gets the same error,
    /// so the endpoint does not reveal which accounts are site admins.
    /// </summary>
    Task<AuthResult> LoginAsync(SiteLoginRequest request, CancellationToken cancellationToken);

    /// <summary>Rotates the site refresh token, with the same reuse detection as the agency sessions.</summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    Task<SiteMeResponse> GetMeAsync(CancellationToken cancellationToken);
}
