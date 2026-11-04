using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

/// <summary>
/// Adding, changing and removing the people of the current organization. All of it needs the <c>members.manage</c>
/// permission, which only the organization admin of a company or an agency holds by default. An individual creator's
/// organization is one person and cannot invite anybody.
/// </summary>
public interface ITeamService
{
    Task<TeamResponse> GetTeamAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Invites an address with a role and returns the secret for the link, once. Refuses an address that already has an
    /// account (email_taken), a role the organization's model does not have, and a creator's organization.
    /// </summary>
    Task<CreatedInvitationResponse> InviteAsync(InviteMemberRequest request, CancellationToken cancellationToken);

    Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken);

    /// <summary>The person signs in again to get the permissions of the new role.</summary>
    Task ChangeRoleAsync(Guid memberUserId, ChangeMemberRoleRequest request, CancellationToken cancellationToken);

    /// <summary>The person's sessions end at once. Their account is deleted when they belong to no other organization.</summary>
    Task RemoveMemberAsync(Guid memberUserId, CancellationToken cancellationToken);
}
