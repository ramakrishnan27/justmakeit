namespace Platform.Auth.Models;

/// <summary>Platform-wide totals, as returned by the site_admin_overview() function.</summary>
public sealed class SiteOverviewRecord
{
    public long AgencyCount { get; set; }
    public long TrialAgencyCount { get; set; }
    public long PaidAgencyCount { get; set; }
    public long UserCount { get; set; }
    public long WorkspaceCount { get; set; }
    public long MemberCount { get; set; }
}
