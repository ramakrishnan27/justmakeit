using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class PermissionRepository(IDbConnectionFactory connections) : IPermissionRepository
{
    public async Task<PermissionSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        // No tenant: permission_snapshot() holds configuration, not tenant data, and a person who is
        // still signing in has no token to act under yet.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        var rows = (await connection.QueryAsync<PermissionSnapshotRow>(new CommandDefinition(
            "SELECT version, business_model, role, permission_key, is_granted FROM permission_snapshot()",
            cancellationToken: cancellationToken))).AsList();

        // The function returns at least one row while the version row exists. None means it is gone, and
        // without a version no token could be checked: fail closed instead of guessing.
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The permission version row is missing from permission_state.");
        }

        var granted = new Dictionary<(BusinessModel, OrganizationRole), List<string>>();
        foreach (var row in rows)
        {
            // A slot or key this build does not know is ignored; it cannot be granted to anybody.
            if (row.IsGranted != true
                || row.PermissionKey is null
                || !BusinessModelRules.TryParse(row.BusinessModel, out var model)
                || !RolePermissions.TryParse(row.Role, out var role))
            {
                continue;
            }

            if (!granted.TryGetValue((model, role), out var keys))
            {
                granted[(model, role)] = keys = [];
            }

            keys.Add(row.PermissionKey);
        }

        return new PermissionSnapshot(
            rows[0].Version,
            granted.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.Ordinal).ToArray()));
    }

    public async Task<PermissionCatalog> GetCatalogAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);

        // The own_select policy lets a user see their own row only, so this is true only for a site admin.
        var isSiteAdmin = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM site_admins WHERE user_id = @userId)", new { userId }, cancellationToken: cancellationToken));
        if (!isSiteAdmin)
        {
            throw NotASiteAdmin();
        }

        var permissions = (await connection.QueryAsync<PermissionRecord>(new CommandDefinition(
            "SELECT key, title, description, is_protected FROM permissions ORDER BY key",
            cancellationToken: cancellationToken))).AsList();
        var grants = (await connection.QueryAsync<GrantRecord>(new CommandDefinition(
            "SELECT business_model, role, permission_key, is_granted FROM role_permissions",
            cancellationToken: cancellationToken))).AsList();

        return new PermissionCatalog(permissions, grants);
    }

    public async Task<bool> SetGrantAsync(
        Guid userId, string businessModel, string role, string permissionKey, bool granted, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT site_admin_set_role_permission(@businessModel, @role, @permissionKey, @granted)",
                new { businessModel, role, permissionKey, granted },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.RaiseException && exception.MessageText == "permission_protected")
        {
            throw new ConflictException("This permission is protected: every role keeps it.", "permission_protected");
        }
    }

    private static ForbiddenException NotASiteAdmin() => new("Site admin access is required.", "not_site_admin");
}
