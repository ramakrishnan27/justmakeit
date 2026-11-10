using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>One row of site_admin_list_members(): a person and the organization they belong to. <see cref="TotalCount"/> repeats on every row.</summary>
public sealed class SiteMemberRecord
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid AgencyId { get; set; }
    public string AgencyName { get; set; } = string.Empty;
    public BusinessModel BusinessModel { get; set; }
    public OrganizationRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public long TotalCount { get; set; }
}
