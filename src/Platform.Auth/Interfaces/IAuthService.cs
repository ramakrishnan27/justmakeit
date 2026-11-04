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

    /// <summary>Issues a new access token for another agency the signed-in user belongs to.</summary>
    Task<AuthResult> SwitchAgencyAsync(SwitchAgencyRequest request, CancellationToken cancellationToken);

    Task<MeResponse> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What the invitation page shows for the secret in a link. Throws <c>NotFoundException</c> (invitation_invalid) for a
    /// link that never existed, was used, was revoked or ran out, so a guess learns nothing.
    /// </summary>
    Task<InvitationPreviewResponse> PreviewInvitationAsync(InvitationTokenRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Accepts an invitation: creates the person with the invited address, the chosen name and password and their
    /// membership, and signs them in. Throws invitation_invalid or email_taken.
    /// </summary>
    Task<AuthResult> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken);
}
