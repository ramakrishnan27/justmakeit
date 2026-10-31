using Platform.Auth.DTOs;
using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token: the presented one is revoked and a new one issued.</summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the refresh token. Never fails for a token that is unknown or already revoked.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Issues tokens for another agency the signed-in user belongs to.</summary>
    Task<AuthResult> SwitchAgencyAsync(SwitchAgencyRequest request, string? currentRefreshToken, CancellationToken cancellationToken);

    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken);
}
