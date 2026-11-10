using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

/// <summary>
/// The service-level check behind every Site admin operation: the token must be a Site admin's. The SiteAdmin policy already
/// stopped everyone else and the database function checks again; this is the same rule at the service, so no other caller
/// can skip it.
/// </summary>
internal static class SiteAdminGuard
{
    public static Guid Require(ITenantContext tenant)
    {
        var userId = tenant.RequireUserId();
        return tenant.Role == RoleNames.SiteAdmin
            ? userId
            : throw new ForbiddenException("Site admin access is required.", "not_site_admin");
    }
}
