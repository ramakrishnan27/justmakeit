using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Results;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

/// <summary>The site admin dashboard: platform totals and a page of every agency.</summary>
public sealed class SiteAdminDashboardService(ISiteAdminRepository siteAdmins, ITenantContext tenant) : ISiteAdminDashboardService
{
    public async Task<SiteOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var userId = RequireSiteAdmin();
        var overview = await siteAdmins.GetOverviewAsync(userId, cancellationToken);

        return new SiteOverviewResponse(
            overview.AgencyCount,
            overview.TrialAgencyCount,
            overview.PaidAgencyCount,
            overview.UserCount,
            overview.WorkspaceCount,
            overview.MemberCount);
    }

    public async Task<PagedResult<SiteAgencyResponse>> ListAgenciesAsync(SiteAgenciesQuery query, CancellationToken cancellationToken)
    {
        query.Validate().ThrowIfAny();

        var userId = RequireSiteAdmin();
        var result = await siteAdmins.ListAgenciesAsync(userId, query.Page, query.PageSize, cancellationToken);

        return new PagedResult<SiteAgencyResponse>
        {
            Items = result.Items
                .Select(a => new SiteAgencyResponse(a.Id, a.Name, a.Plan, a.CreatedAt, a.MemberCount, a.WorkspaceCount))
                .ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = result.TotalCount
        };
    }

    // The SiteAdmin policy already stopped everyone else, and the database function checks again.
    // This is the same rule at the service, so no other caller can skip it.
    private Guid RequireSiteAdmin()
    {
        var userId = tenant.RequireUserId();
        return tenant.Role == RoleNames.SiteAdmin
            ? userId
            : throw new ForbiddenException("Site admin access is required.", "not_site_admin");
    }
}
