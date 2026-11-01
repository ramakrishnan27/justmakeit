using Platform.Auth.DTOs;
using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an organization and its founder from an outside sign-in (Google) instead of a password.
    /// Throws a conflict (email_taken) when the email already has an account, however that account signs in.
    /// </summary>
    Task<AuthResult> RegisterExternalAsync(ExternalRegistration registration, CancellationToken cancellationToken);

    /// <summary>Starts a session for a user the outside provider has already identified, in their most recently used organization.</summary>
    Task<AuthResult> LoginExternalAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token: the presented one is revoked and a new one issued.</summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the refresh token. Never fails for a token that is unknown or already revoked.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);

    /// <summary>Issues tokens for another agency the signed-in user belongs to.</summary>
    Task<AuthResult> SwitchAgencyAsync(SwitchAgencyRequest request, string? currentRefreshToken, CancellationToken cancellationToken);

    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken);
}
