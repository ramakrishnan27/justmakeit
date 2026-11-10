using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface ISiteAdminRepository
{
    /// <summary>True when the user has a row in site_admins. Their own row is the only one they can see.</summary>
    Task<bool> IsSiteAdminAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Platform-wide totals. Throws <see cref="Platform.Helpers.Exceptions.ForbiddenException"/> when the
    /// database refuses because the caller is not a site admin.
    /// </summary>
    Task<SiteOverviewRecord> GetOverviewAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>One page of every agency, newest first. Same refusal rule as <see cref="GetOverviewAsync"/>.</summary>
    Task<SiteAgencyPage> ListAgenciesAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of every membership on the platform (a person and their organization), newest first. Same refusal rule as <see cref="GetOverviewAsync"/>.</summary>
    Task<SiteMemberPage> ListMembersAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates or activates an organization; deactivating ends every member's sessions in the same transaction. True when the state
    /// changed. Throws not found (<c>organization_not_found</c>) for an unknown organization.
    /// </summary>
    Task<bool> SetOrganizationActiveAsync(Guid userId, Guid agencyId, bool isActive, CancellationToken cancellationToken);
}
