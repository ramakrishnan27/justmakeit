using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

/// <summary>Deactivating and activating a whole organization, which only the Site admin can do.</summary>
public sealed class SiteOrganizationService(
    ISiteAdminRepository siteAdmins,
    ISessionRevocationService revocations,
    ITenantContext tenant,
    ILogger<SiteOrganizationService> logger) : ISiteOrganizationService
{
    public async Task SetActiveAsync(Guid agencyId, SetOrganizationActiveRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = SiteAdminGuard.Require(tenant);

        var isActive = request.IsActive!.Value;
        try
        {
            var changed = await siteAdmins.SetOrganizationActiveAsync(userId, agencyId, isActive, cancellationToken);
            logger.LogInformation(
                "Site admin {UserId} {Action} organization {AgencyId} (changed: {Changed})",
                userId, isActive ? "activated" : "deactivated", agencyId, changed);
        }
        finally
        {
            // The sessions end in the database, even when the call failed half way; the list the API holds in memory must learn of it at once.
            revocations.Invalidate();
        }
    }
}
