using Platform.Auth.DTOs;
using Platform.Helpers.Results;

namespace Platform.Auth.Interfaces;

public interface ISiteAdminDashboardService
{
    Task<SiteOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);

    Task<PagedResult<SiteAgencyResponse>> ListAgenciesAsync(SiteAgenciesQuery query, CancellationToken cancellationToken);
}
