using Platform.Auth.DTOs;
using Platform.Helpers.Results;

namespace Platform.Auth.Interfaces;

public interface ISiteAdminDashboardService
{
    Task<SiteOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);

    Task<PagedResult<SiteAgencyResponse>> ListAgenciesAsync(SiteAgenciesQuery query, CancellationToken cancellationToken);

    /// <summary>One page of every person on the platform with the organization they belong to.</summary>
    Task<PagedResult<SiteMemberResponse>> ListMembersAsync(SiteMembersQuery query, CancellationToken cancellationToken);
}
