using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Media.Services;

/// <summary>
/// The check every Media service makes first. The endpoint's policy has already looked at the access token; this is the same rule again,
/// so no other caller of a service can skip it, and it returns the organization and user the work is for.
/// </summary>
public static class MediaAccess
{
    public static (Guid AgencyId, Guid UserId) Require(ITenantContext tenant, string permission, string what)
    {
        var agencyId = tenant.RequireAgencyId();
        var userId = tenant.RequireUserId();

        if (!tenant.HasPermission(permission))
        {
            throw new ForbiddenException($"Your role is not allowed to {what}.", "insufficient_permission");
        }

        return (agencyId, userId);
    }
}
