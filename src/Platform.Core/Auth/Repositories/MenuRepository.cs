using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// The menu tables. Reading for the cache needs no user (configuration, not tenant data). The Site admin's
/// reads check <c>site_admins</c> on the same connection, and every write is a SECURITY DEFINER function
/// that checks it inside the database, so a demoted site admin with a token that has not expired yet gets 403.
/// </summary>
public sealed class MenuRepository(IDbConnectionFactory connections) : IMenuRepository
{
    private const string ItemColumns =
        "i.menu_key, i.id, i.page_key, p.title AS page_title, p.path, p.permission_key, i.label, i.icon, i.sort_order, i.is_visible, i.is_locked";

    public async Task<MenuSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        var items = (await connection.QueryAsync<MenuItemRecord>(new CommandDefinition(
            """
            SELECT menu_key, id, page_key, page_title, path, permission_key, label, icon, sort_order, is_visible, is_locked
            FROM menu_snapshot()
            """,
            cancellationToken: cancellationToken))).AsList();
        return new MenuSnapshot(items);
    }

    public async Task<MenuConfig> GetConfigAsync(Guid userId, string menuKey, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        await RequireSiteAdminAsync(connection, userId, cancellationToken);

        var items = (await connection.QueryAsync<MenuItemRecord>(new CommandDefinition(
            $"""
            SELECT {ItemColumns}
            FROM menu_items i
            JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key
            WHERE i.menu_key = @menuKey
            ORDER BY i.sort_order, i.id
            """,
            new { menuKey },
            cancellationToken: cancellationToken))).AsList();

        var unused = (await connection.QueryAsync<MenuPageRecord>(new CommandDefinition(
            """
            SELECT p.app, p.key, p.title, p.path, p.permission_key
            FROM menu_pages p
            WHERE p.app = @menuKey
              AND NOT EXISTS (SELECT 1 FROM menu_items i WHERE i.menu_key = p.app AND i.page_key = p.key)
            ORDER BY p.default_sort_order, p.key
            """,
            new { menuKey },
            cancellationToken: cancellationToken))).AsList();

        return new MenuConfig(items, unused);
    }

    public async Task<MenuItemRecord?> FindItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        await RequireSiteAdminAsync(connection, userId, cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<MenuItemRecord>(new CommandDefinition(
            $"""
            SELECT {ItemColumns}
            FROM menu_items i
            JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key
            WHERE i.id = @itemId
            """,
            new { itemId },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateItemAsync(
        Guid userId, string menuKey, string pageKey, string label, string icon, bool isVisible, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "SELECT site_admin_create_menu_item(@menuKey, @pageKey, @label, @icon, @isVisible)",
                new { menuKey, pageKey, label, icon, isVisible },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.ForeignKeyViolation && exception.ConstraintName == "fk_menu_items_page")
        {
            throw new NotFoundException("That page does not exist for this menu.", "page_not_found");
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == "uq_menu_items_menu_page")
        {
            throw new ConflictException("That page is already in the menu.", "page_already_in_menu");
        }
    }

    public async Task<bool> UpdateItemAsync(
        Guid userId, Guid itemId, string label, string icon, bool? isVisible, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT site_admin_update_menu_item(@itemId, @label, @icon, @isVisible)",
                new { itemId, label, icon, isVisible },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
        catch (PostgresException exception) when (IsRule(exception, "menu_item_locked"))
        {
            throw Locked();
        }
    }

    public async Task<bool> DeleteItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT site_admin_delete_menu_item(@itemId)", new { itemId }, cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
        catch (PostgresException exception) when (IsRule(exception, "menu_item_locked"))
        {
            throw Locked();
        }
    }

    public async Task ReorderAsync(Guid userId, string menuKey, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT site_admin_reorder_menu(@menuKey, @itemIds)",
                new { menuKey, itemIds = JsonbParameter.From(itemIds.Select(id => id.ToString()).ToArray()) },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
        catch (PostgresException exception) when (IsRule(exception, "menu_order_mismatch"))
        {
            throw new ConflictException("The menu changed since you opened it. Reload it and try again.", "menu_changed");
        }
    }

    private static async Task RequireSiteAdminAsync(NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken)
    {
        // The own_select policy lets a user see their own row only, so this is true only for a site admin.
        var isSiteAdmin = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM site_admins WHERE user_id = @userId)", new { userId }, cancellationToken: cancellationToken));
        if (!isSiteAdmin)
        {
            throw NotASiteAdmin();
        }
    }

    /// <summary>A rule the database function enforces: it raises P0001 with a short code as the message.</summary>
    private static bool IsRule(PostgresException exception, string code) =>
        exception.SqlState == PostgresErrorCodes.RaiseException && exception.MessageText == code;

    private static ConflictException Locked() =>
        new("This menu item is locked: it can be renamed and moved, but not hidden or deleted.", "menu_item_locked");

    private static ForbiddenException NotASiteAdmin() => new("Site admin access is required.", "not_site_admin");
}
