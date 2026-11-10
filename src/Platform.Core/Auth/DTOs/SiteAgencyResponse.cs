using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.DTOs;

/// <summary>
/// One organization as the site admin sees it (called an agency in the code). <paramref name="IsActive"/> is false when the
/// Site admin has deactivated it; <paramref name="Channels"/> is how many channels are enabled for it.
/// </summary>
public sealed record SiteAgencyResponse(
    Guid Id, string Name, BusinessModel BusinessModel, AgencyPlan Plan, DateTimeOffset CreatedAt, long Members, long Workspaces,
    bool IsActive, long Channels);
