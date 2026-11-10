using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// What the Site admin does to one organization: enable or disable its channels, and deactivate or activate it. The list of
/// organizations itself is <c>GET /v1/site/agencies</c> (<see cref="SiteController"/>). Every route needs the
/// <see cref="PolicyNames.SiteAdmin"/> policy, shares the session rate limit and is marked no-store.
/// </summary>
[ApiController]
[Route("v1/site/agencies")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = PolicyNames.SiteAdmin)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class SiteOrganizationsController(ISiteChannelService channels, ISiteOrganizationService organizations) : ControllerBase
{
    /// <summary>Every channel, with whether it is enabled for this organization.</summary>
    [HttpGet("{agencyId:guid}/channels")]
    [ProducesResponseType<IReadOnlyList<OrganizationChannelResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrganizationChannelResponse>>> ListChannels(Guid agencyId, CancellationToken cancellationToken) =>
        Ok(await channels.ListForOrganizationAsync(agencyId, cancellationToken));

    /// <summary>Enables or disables one channel for the organization. Repeating the same call changes nothing.</summary>
    [HttpPut("{agencyId:guid}/channels/{channelId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetChannel(
        Guid agencyId, Guid channelId, SetOrganizationChannelRequest request, CancellationToken cancellationToken)
    {
        await channels.SetForOrganizationAsync(agencyId, channelId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Deactivates or activates the organization. Deactivating signs every member out at once; their data is kept.</summary>
    [HttpPut("{agencyId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetStatus(Guid agencyId, SetOrganizationActiveRequest request, CancellationToken cancellationToken)
    {
        await organizations.SetActiveAsync(agencyId, request, cancellationToken);
        return NoContent();
    }
}
