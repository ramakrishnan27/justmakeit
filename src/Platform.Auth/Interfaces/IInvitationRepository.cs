using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

/// <summary>
/// Invitations before anybody is signed in. The token is the credential, so both calls take the SHA-256 hash of it, and
/// a missing, used, revoked or expired invitation looks the same.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>The pending invitation behind this hash, or null.</summary>
    Task<InvitationPreviewRecord?> FindAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the person (with the invited address, never one the caller chose) and their membership, and marks the
    /// invitation used, in one step. Throws <c>NotFoundException</c> (invitation_invalid) when it is no longer pending
    /// and <c>ConflictException</c> (email_taken) when the address became an account in the meantime.
    /// </summary>
    Task<AcceptedInvitationRecord> AcceptAsync(
        string tokenHash, Guid userId, string fullName, string passwordHash, CancellationToken cancellationToken);
}
