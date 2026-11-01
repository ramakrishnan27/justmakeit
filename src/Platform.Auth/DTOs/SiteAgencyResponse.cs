using Platform.Auth.Models;

namespace Platform.Auth.DTOs;

public sealed record SiteAgencyResponse(
    Guid Id, string Name, AgencyPlan Plan, DateTimeOffset CreatedAt, long Members, long Workspaces);
