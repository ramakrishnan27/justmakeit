using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

public sealed class MenuService(IMenuCache menus, ITenantContext tenant) : IMenuService
{
    public async Task<MenuResponse> GetMemberMenuAsync(CancellationToken cancellationToken)
    {
        tenant.RequireAgencyId();
        tenant.RequireUserId();

        // The permissions are the ones in the caller's token, the same ones every endpoint checks, so the
        // menu can never offer what the API would refuse. Nothing here reads the database except a cache miss.
        var permissions = tenant.Permissions;
        var snapshot = await menus.GetAsync(cancellationToken);

        return new MenuResponse(
            MenuVisibility.ForMember(snapshot.Items, permissions).Select(ToResponse).ToList(),
            permissions.Order(StringComparer.Ordinal).ToList());
    }

    public async Task<MenuResponse> GetSiteAdminMenuAsync(CancellationToken cancellationToken)
    {
        tenant.RequireUserId();
        if (tenant.Role != RoleNames.SiteAdmin)
        {
            throw new ForbiddenException("Site admin access is required.", "not_site_admin");
        }

        var snapshot = await menus.GetAsync(cancellationToken);

        // A site admin's rights are the role itself, so there are no permissions to report.
        return new MenuResponse(MenuVisibility.ForSiteAdmin(snapshot.Items).Select(ToResponse).ToList(), []);
    }

    private static MenuItemResponse ToResponse(MenuItemRecord item) => new(item.Id, item.PageKey, item.Label, item.Icon, item.Path);
}
