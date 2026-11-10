using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// Channels for the Site admin. No agency is involved: the connection carries only the user, and each function of
/// <c>011_channels.sql</c> decides from that user alone whether the call may happen. The tables themselves are closed to the
/// application role.
/// </summary>
public sealed class SiteChannelRepository(IDbConnectionFactory connections) : ISiteChannelRepository
{
    private const string UniqueNameIndex = "ux_channels_name";

    public async Task<IReadOnlyList<SiteChannelRecord>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return (await connection.QueryAsync<SiteChannelRecord>(new CommandDefinition(
                """
                SELECT id, name, description, icon_key, has_image, created_by_name, created_at, organization_count
                FROM site_admin_list_channels()
                """,
                cancellationToken: cancellationToken))).AsList();
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<SiteChannelRecord> CreateAsync(Guid userId, NewChannel channel, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.QuerySingleAsync<SiteChannelRecord>(new CommandDefinition(
                """
                SELECT id, name, description, icon_key, has_image, created_by_name, created_at, organization_count
                FROM site_admin_create_channel(@name, @description, @iconKey, @image, @contentType)
                """,
                new { name = channel.Name, description = channel.Description, iconKey = channel.IconKey, image = channel.Image, contentType = channel.ContentType },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<ChannelIconRecord?> GetIconAsync(Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.QuerySingleOrDefaultAsync<ChannelIconRecord>(new CommandDefinition(
                "SELECT content_type, image FROM site_admin_channel_icon(@channelId)",
                new { channelId },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<IReadOnlyList<OrganizationChannelRecord>> ListForOrganizationAsync(
        Guid userId, Guid agencyId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return (await connection.QueryAsync<OrganizationChannelRecord>(new CommandDefinition(
                """
                SELECT id, name, description, icon_key, has_image, is_enabled
                FROM site_admin_organization_channels(@agencyId)
                """,
                new { agencyId },
                cancellationToken: cancellationToken))).AsList();
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<bool> SetForOrganizationAsync(
        Guid userId, Guid agencyId, Guid channelId, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT site_admin_set_organization_channel(@agencyId, @channelId, @enabled)",
                new { agencyId, channelId, enabled },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>
    /// The rules the channel functions enforce. A rule is raised as P0001 with a short code as the message; "not a site admin" is
    /// 42501; a taken name is the unique index; a row that vanished between the check and the write is a foreign key violation.
    /// Anything else is not ours.
    /// </summary>
    private static AppException? Translate(PostgresException exception)
    {
        if (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new ForbiddenException("Site admin access is required.", "not_site_admin");
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == UniqueNameIndex)
        {
            return new ConflictException("A channel with that name already exists.", "channel_exists");
        }

        if (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // organization_channels_agency_id_fkey or organization_channels_channel_id_fkey.
            return exception.ConstraintName?.EndsWith("channel_id_fkey", StringComparison.Ordinal) == true
                ? ChannelNotFound()
                : OrganizationNotFound();
        }

        if (exception.SqlState != PostgresErrorCodes.RaiseException)
        {
            return null;
        }

        return exception.MessageText switch
        {
            "organization_not_found" => OrganizationNotFound(),
            "channel_not_found" => ChannelNotFound(),
            _ => null
        };
    }

    private static NotFoundException OrganizationNotFound() => new("That organization does not exist.", "organization_not_found");

    private static NotFoundException ChannelNotFound() => new("That channel does not exist.", "channel_not_found");
}
