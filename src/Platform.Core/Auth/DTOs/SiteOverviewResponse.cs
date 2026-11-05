namespace Platform.Auth.DTOs;

public sealed record SiteOverviewResponse(
    long Agencies, long TrialAgencies, long PaidAgencies, long Users, long Workspaces, long Memberships);
