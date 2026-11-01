namespace Platform.Auth.Models;

/// <summary>One row of site_admin_list_agencies(). <see cref="TotalCount"/> repeats on every row.</summary>
public sealed class SiteAgencyRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AgencyPlan Plan { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long MemberCount { get; set; }
    public long WorkspaceCount { get; set; }
    public long TotalCount { get; set; }
}
