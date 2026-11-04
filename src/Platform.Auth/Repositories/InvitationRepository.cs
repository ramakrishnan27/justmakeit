using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// Finding and accepting an invitation, before anybody is signed in. Both are SECURITY DEFINER functions of
/// <c>007_team_members.sql</c> that match one exact token hash and return only what the page needs.
/// </summary>
public sealed class InvitationRepository(IDbConnectionFactory connections) : IInvitationRepository
{
    private const string UniqueEmailIndex = "ux_users_email";

    public async Task<InvitationPreviewRecord?> FindAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InvitationPreviewRecord>(new CommandDefinition(
            "SELECT id, agency_id, organization_name, business_model, email, role, expires_at FROM invitation_find(@tokenHash)",
            new { tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task<AcceptedInvitationRecord> AcceptAsync(
        string tokenHash, Guid userId, string fullName, string passwordHash, CancellationToken cancellationToken)
    {
        // The connection acts as the new user. Their sign-in method is checked when the statement commits, as the
        // connection's user, and a user sees their own rows only: this is the same way registration does it.
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.QuerySingleAsync<AcceptedInvitationRecord>(new CommandDefinition(
                "SELECT agency_id, role FROM invitation_accept(@tokenHash, @userId, @fullName, @passwordHash)",
                new { tokenHash, userId, fullName, passwordHash },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.RaiseException && exception.MessageText == "invitation_invalid")
        {
            throw InvitationErrors.Invalid();
        }
        catch (PostgresException exception)
            when ((exception.SqlState == PostgresErrorCodes.RaiseException && exception.MessageText == "email_taken")
                  || (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == UniqueEmailIndex))
        {
            throw new ConflictException("An account with this email already exists.", "email_taken");
        }
    }
}
