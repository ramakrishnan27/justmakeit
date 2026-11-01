using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Paging for the site admin's agency list, taken from the query string: <c>?page=2&amp;pageSize=20</c>.</summary>
public sealed record SiteAgenciesQuery(int Page = 1, int PageSize = SiteAgenciesQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (Page < 1)
        {
            errors.Add("page", "Page must be 1 or more.");
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }

        return errors;
    }
}
