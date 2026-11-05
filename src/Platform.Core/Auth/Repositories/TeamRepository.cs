using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// The team of an organization. Listing, changing and removing people go through the <c>team_*</c> SECURITY DEFINER
/// functions of <c>007_team_members.sql</c> and <c>008_team_admin.sql</c> (the <c>_v2</c> ones are what the API calls), which check inside the database that the caller holds
/// <c>members.manage</c> in the organization the connection acts for; invitations are plain rows under row-level
/// security, like any other tenant table.
/// </summary>
public sealed class TeamRepository(IDbConnectionFactory connections) : ITeamRepository
{
    private const string PendingInvitationIndex = "ux_member_invitations_pending";
    private const string RoleModelCheck = "ck_agency_members_role_model";
    private const string UniqueEmailIndex = "ux_users_email";

    public async Task<IReadOnlyList<TeamMemberRecord>> ListMembersAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            var rows = await connection.QueryAsync<TeamMemberRecord>(new CommandDefinition(
                "SELECT user_id, email, full_name, role, joined_at, is_active FROM team_members_v2()",
                cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotAllowed();
        }
    }

    public async Task<IReadOnlyList<InvitationRecord>> ListPendingInvitationsAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        // The agency filter is written out even though row-level security enforces it as well.
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var rows = await connection.QueryAsync<InvitationRecord>(new CommandDefinition(
            """
            SELECT id, coalesce(full_name, '') AS full_name, email, role, created_at, expires_at
            FROM member_invitations
            WHERE agency_id = @agencyId AND accepted_at IS NULL AND revoked_at IS NULL
            ORDER BY created_at DESC, id
            """,
            new { agencyId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> EmailIsRegisteredAsync(string email, CancellationToken cancellationToken)
    {
        // No tenant needed: the function answers one exact address with a yes or a no.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT auth_email_registered(@email)", new { email }, cancellationToken: cancellationToken));
    }

    public async Task InsertInvitationAsync(NewInvitation invitation, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(invitation.AgencyId, invitation.InvitedBy, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // One invitation per address is pending at a time; an expired one can be replaced, a live one cannot.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE member_invitations SET revoked_at = @now
                WHERE agency_id = @AgencyId AND lower(email) = lower(@Email)
                  AND accepted_at IS NULL AND revoked_at IS NULL AND expires_at <= @now
                """,
                new { now = invitation.CreatedAt, invitation.AgencyId, invitation.Email },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO member_invitations (id, agency_id, business_model, full_name, email, role, token_hash, invited_by, created_at, expires_at)
                VALUES (@Id, @AgencyId, @businessModel, @FullName, @Email, @role, @TokenHash, @InvitedBy, @CreatedAt, @ExpiresAt)
                """,
                new
                {
                    invitation.Id,
                    invitation.AgencyId,
                    businessModel = invitation.BusinessModel.ToDb(),
                    invitation.FullName,
                    invitation.Email,
                    role = invitation.Role.ToDb(),
                    invitation.TokenHash,
                    invitation.InvitedBy,
                    invitation.CreatedAt,
                    invitation.ExpiresAt
                },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == PendingInvitationIndex)
        {
            throw new ConflictException(
                "An invitation for this address is already waiting. Revoke it first to send another.", "invitation_exists");
        }
    }

    public async Task<bool> RevokeInvitationAsync(
        Guid agencyId, Guid userId, Guid invitationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE member_invitations SET revoked_at = @now
            WHERE id = @invitationId AND agency_id = @agencyId AND accepted_at IS NULL AND revoked_at IS NULL
            """,
            new { now, invitationId, agencyId },
            cancellationToken: cancellationToken));
        return changed > 0;
    }

    public async Task ChangeRoleAsync(Guid agencyId, Guid userId, Guid memberUserId, OrganizationRole role, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT team_change_role_v2(@memberUserId, @role)",
                new { memberUserId, role = role.ToDb() },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task SetMemberActiveAsync(
        Guid agencyId, Guid userId, Guid memberUserId, bool isActive, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT team_set_member_active(@memberUserId, @isActive)",
                new { memberUserId, isActive },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task ActivateInvitationAsync(
        Guid agencyId, Guid userId, Guid invitationId, Guid newUserId, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT team_activate_invitation(@invitationId, @newUserId, @passwordHash)",
                new { invitationId, newUserId, passwordHash },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == UniqueEmailIndex)
        {
            // Two people were activated for the same address at the same moment.
            throw new ConflictException("This email already has an account.", "email_taken");
        }
    }

    public async Task RemoveMemberAsync(Guid agencyId, Guid userId, Guid memberUserId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT team_remove_member_v2(@memberUserId)", new { memberUserId }, cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// The rules the team functions enforce. A rule is raised as P0001 with a short code as the message; "not allowed"
    /// is 42501; a role the organization's model does not have is the role/model CHECK. Anything else is not ours.
    /// </summary>
    private static AppException? Translate(PostgresException exception)
    {
        if (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return NotAllowed();
        }

        if (exception.SqlState == PostgresErrorCodes.CheckViolation && exception.ConstraintName == RoleModelCheck)
        {
            return new BadRequestException(
                "That role is not available in this kind of organization.",
                "role_not_allowed",
                new Dictionary<string, string[]> { ["role"] = ["That role is not available in this kind of organization."] });
        }

        if (exception.SqlState != PostgresErrorCodes.RaiseException)
        {
            return null;
        }

        return exception.MessageText switch
        {
            "member_not_found" => new NotFoundException("That person is not a member of this organization.", "member_not_found"),
            "cannot_change_self" => new ConflictException("You cannot change or remove yourself.", "cannot_change_self"),
            "last_organization_admin" => new ConflictException(
                "An organization needs at least one active organization admin.", "last_organization_admin"),
            "invitation_not_found" => new NotFoundException("That invitation is not waiting any more.", "invitation_not_found"),
            "email_taken" => new ConflictException("This email already has an account.", "email_taken"),
            _ => null
        };
    }

    private static ForbiddenException NotAllowed() =>
        new("Your role is not allowed to manage the team.", "insufficient_permission");
}
