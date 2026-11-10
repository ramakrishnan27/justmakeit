namespace Platform.Auth.Models;

public sealed record SiteMemberPage(IReadOnlyList<SiteMemberRecord> Items, long TotalCount);
