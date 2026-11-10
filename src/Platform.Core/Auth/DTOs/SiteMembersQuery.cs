using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Paging for the site admin's member list, taken from the query string: <c>?page=2&amp;pageSize=20</c>. The same limits as the agency list.</summary>
public sealed record SiteMembersQuery(int Page = 1, int PageSize = SiteAgenciesQuery.DefaultPageSize)
{
    public ValidationErrors Validate() => new SiteAgenciesQuery(Page, PageSize).Validate();
}
