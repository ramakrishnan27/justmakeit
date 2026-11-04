using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;
using Platform.Helpers.Validation;

namespace Platform.Auth.Services;

public sealed class TeamService(
    ITeamRepository team,
    IAgencyRepository agencies,
    ISessionRevocationService revocations,
    IPasswordHasher passwordHasher,
    ITenantContext tenant,
    IOptions<InvitationSettings> options,
    TimeProvider time,
    ILogger<TeamService> logger) : ITeamService
{
    public async Task<TeamResponse> GetTeamAsync(CancellationToken cancellationToken)
    {
        var (agencyId, userId) = RequireManager();

        var members = await team.ListMembersAsync(agencyId, userId, cancellationToken);
        var invitations = await team.ListPendingInvitationsAsync(agencyId, userId, cancellationToken);

        var now = time.GetUtcNow();
        return new TeamResponse(
            members.Select(m => new TeamMemberResponse(m.UserId, m.Email, m.FullName, m.Role, m.JoinedAt, m.UserId == userId, m.IsActive)).ToList(),
            invitations.Select(i => ToResponse(i, now)).ToList());
    }

    public async Task<CreatedInvitationResponse> InviteAsync(InviteMemberRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var (agencyId, userId) = RequireManager();

        // Validate() has accepted the text, so it names a role.
        var role = RolePermissions.TryParse(request.Role, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The role was validated but could not be read.");

        var agency = await agencies.GetAsync(agencyId, userId, cancellationToken)
                     ?? throw new UnauthorizedException("You are no longer a member of this organization.", "membership_ended");

        // An individual creator is one person. No role reaches this point for a creator today; the rule is checked on
        // its own so it still holds if the Site admin grants the permission to the creator's role.
        if (agency.BusinessModel == BusinessModel.Creator)
        {
            throw new ConflictException("An individual creator works alone and cannot invite people.", "team_not_available");
        }

        RequireRoleOfModel(agency.BusinessModel, role);

        var email = EmailAddressHelper.Normalize(request.Email);

        // One account per address. It does tell the admin that the address is registered; registering says the same
        // to anyone (409 email_taken), and the credentials rate limit is the mitigation there.
        if (await team.EmailIsRegisteredAsync(email, cancellationToken))
        {
            throw new ConflictException("This email already has an account.", "email_taken");
        }

        var token = TokenGenerator.Create();
        var now = time.GetUtcNow();
        var invitation = new NewInvitation(
            Guid.NewGuid(),
            agencyId,
            agency.BusinessModel,
            request.FullName!.Trim(),
            email,
            role,
            HashHelper.Sha256Hex(token),
            userId,
            now,
            now.AddDays(options.Value.InvitationDays));

        await team.InsertInvitationAsync(invitation, cancellationToken);

        // Ids only: neither the token nor the address is logged.
        logger.LogInformation("User {UserId} invited a {Role} to organization {AgencyId} (invitation {InvitationId})", userId, role, agencyId, invitation.Id);
        return new CreatedInvitationResponse(
            new InvitationResponse(invitation.Id, invitation.FullName, email, role, invitation.CreatedAt, invitation.ExpiresAt, IsExpired: false),
            token);
    }

    public async Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = RequireManager();

        if (!await team.RevokeInvitationAsync(agencyId, userId, invitationId, time.GetUtcNow(), cancellationToken))
        {
            throw new NotFoundException("That invitation is not waiting any more.", "invitation_not_found");
        }

        logger.LogInformation("User {UserId} revoked invitation {InvitationId} of organization {AgencyId}", userId, invitationId, agencyId);
    }

    public async Task ActivateInvitationAsync(Guid invitationId, ActivateInvitationRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var (agencyId, userId) = RequireManager();

        var newUserId = Guid.NewGuid();
        try
        {
            await team.ActivateInvitationAsync(
                agencyId, userId, invitationId, newUserId, passwordHasher.Hash(request.Password!), cancellationToken);
        }
        finally
        {
            // The list of ended sessions does not change here, but a new member exists: keep the rule the same for every team change.
            revocations.Invalidate();
        }

        // Ids only: neither the password nor the address is logged.
        logger.LogInformation(
            "User {UserId} activated invitation {InvitationId} of organization {AgencyId} by hand as {NewUserId}", userId, invitationId, agencyId, newUserId);
    }

    public async Task SetMemberActiveAsync(Guid memberUserId, SetMemberActiveRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var (agencyId, userId) = RequireManager();
        RequireOtherPerson(memberUserId, userId);

        try
        {
            await team.SetMemberActiveAsync(agencyId, userId, memberUserId, request.IsActive!.Value, cancellationToken);
        }
        finally
        {
            // Deactivating ends the person's sessions in the database; the in-memory list must learn of it at once.
            revocations.Invalidate();
        }

        logger.LogInformation(
            "User {UserId} {Change} {MemberUserId} in organization {AgencyId}",
            userId, request.IsActive!.Value ? "activated" : "deactivated", memberUserId, agencyId);
    }

    public async Task ChangeRoleAsync(Guid memberUserId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var (agencyId, userId) = RequireManager();
        RequireOtherPerson(memberUserId, userId);

        var role = RolePermissions.TryParse(request.Role, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The role was validated but could not be read.");

        var agency = await agencies.GetAsync(agencyId, userId, cancellationToken)
                     ?? throw new UnauthorizedException("You are no longer a member of this organization.", "membership_ended");
        RequireRoleOfModel(agency.BusinessModel, role);

        try
        {
            await team.ChangeRoleAsync(agencyId, userId, memberUserId, role, cancellationToken);
        }
        finally
        {
            // Even a refused attempt: the cache must never hold a view that is older than the database.
            revocations.Invalidate();
        }

        logger.LogInformation("User {UserId} changed the role of {MemberUserId} in organization {AgencyId} to {Role}", userId, memberUserId, agencyId, role);
    }

    public async Task RemoveMemberAsync(Guid memberUserId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = RequireManager();
        RequireOtherPerson(memberUserId, userId);

        try
        {
            await team.RemoveMemberAsync(agencyId, userId, memberUserId, cancellationToken);
        }
        finally
        {
            revocations.Invalidate();
        }

        logger.LogInformation("User {UserId} removed {MemberUserId} from organization {AgencyId}", userId, memberUserId, agencyId);
    }

    // The members.manage policy already stopped everyone else; this is the same rule, checked again here so no other
    // caller of the service can skip it. The database checks it a third time inside the team functions.
    private (Guid AgencyId, Guid UserId) RequireManager()
    {
        var agencyId = tenant.RequireAgencyId();
        var userId = tenant.RequireUserId();

        if (!tenant.HasPermission(PermissionKeys.MembersManage))
        {
            throw new ForbiddenException("Your role is not allowed to manage the team.", "insufficient_permission");
        }

        return (agencyId, userId);
    }

    private static void RequireOtherPerson(Guid memberUserId, Guid userId)
    {
        if (memberUserId == userId)
        {
            throw new ConflictException("You cannot change or remove yourself.", "cannot_change_self");
        }
    }

    private static void RequireRoleOfModel(BusinessModel model, OrganizationRole role)
    {
        if (!model.IsAllowed(role))
        {
            var errors = new ValidationErrors();
            errors.Add("role", "That role is not available in this kind of organization.");
            errors.ThrowIfAny();
        }
    }

    private static InvitationResponse ToResponse(InvitationRecord invitation, DateTimeOffset now) =>
        new(invitation.Id, invitation.FullName, invitation.Email, invitation.Role, invitation.CreatedAt, invitation.ExpiresAt, invitation.ExpiresAt <= now);
}
