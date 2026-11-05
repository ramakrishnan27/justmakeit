using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Interfaces;

/// <summary>
/// The team of the current organization. Every call acts for the given organization and user; the database checks
/// that user holds <c>members.manage</c> in it (the team functions) or that the row belongs to the organization
/// (row-level security), so a caller that skipped the service's own check is still refused.
/// </summary>
public interface ITeamRepository
{
    /// <summary>The people in the organization. Throws <c>ForbiddenException</c> (insufficient_permission) when the user may not manage the team.</summary>
    Task<IReadOnlyList<TeamMemberRecord>> ListMembersAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The invitations that are neither accepted nor revoked, newest first. Some may have expired.</summary>
    Task<IReadOnlyList<InvitationRecord>> ListPendingInvitationsAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Whether this address is already an account (of any organization). Case does not matter.</summary>
    Task<bool> EmailIsRegisteredAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Writes an invitation, first revoking an expired one for the same address so it can be replaced. Throws
    /// <c>ConflictException</c> (invitation_exists) when a live one is still waiting.
    /// </summary>
    Task InsertInvitationAsync(NewInvitation invitation, CancellationToken cancellationToken);

    /// <summary>Revokes a pending invitation of the organization. False when there is none such.</summary>
    Task<bool> RevokeInvitationAsync(Guid agencyId, Guid userId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates (false) or activates (true) a member. Deactivating ends their sessions. Throws member_not_found,
    /// cannot_change_self or last_organization_admin (the last ACTIVE admin cannot be deactivated).
    /// </summary>
    Task SetMemberActiveAsync(Guid agencyId, Guid userId, Guid memberUserId, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Turns a waiting invitation into a member with the given password hash, as accepting it would, and marks the
    /// invitation used. Throws invitation_not_found or email_taken.
    /// </summary>
    Task ActivateInvitationAsync(
        Guid agencyId, Guid userId, Guid invitationId, Guid newUserId, string passwordHash, CancellationToken cancellationToken);

    /// <summary>
    /// Gives a member another role and ends that person's sessions. Throws member_not_found, cannot_change_self,
    /// last_organization_admin or a role the organization's model does not allow.
    /// </summary>
    Task ChangeRoleAsync(Guid agencyId, Guid userId, Guid memberUserId, OrganizationRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a member and ends their sessions; deletes the account when they belong to no other organization.
    /// Throws member_not_found, cannot_change_self or last_organization_admin.
    /// </summary>
    Task RemoveMemberAsync(Guid agencyId, Guid userId, Guid memberUserId, CancellationToken cancellationToken);
}
