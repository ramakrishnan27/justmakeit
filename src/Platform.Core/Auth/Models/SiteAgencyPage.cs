namespace Platform.Auth.Models;

public sealed record SiteAgencyPage(IReadOnlyList<SiteAgencyRecord> Items, long TotalCount);
