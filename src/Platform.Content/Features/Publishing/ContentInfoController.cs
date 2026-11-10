using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Content.DTOs;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Content.Features.Publishing;

/// <summary>What the pages need before they draw: which parts of Content Studio are real here, and each platform's limits.</summary>
[ApiController]
[Route("v1/content")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContentInfoController(ICapabilitiesService capabilities) : ControllerBase
{
    /// <summary>
    /// What is real in this installation: <c>media</c>, <c>ai</c> and <c>campaigns</c> are false until those modules exist, and
    /// <c>publishing</c> is <c>none</c>, <c>fake</c> (the development stand-in) or <c>live</c>.
    /// </summary>
    [HttpGet("capabilities")]
    [ProducesResponseType<CapabilitiesResponse>(StatusCodes.Status200OK)]
    public ActionResult<CapabilitiesResponse> Capabilities() => Ok(capabilities.GetCapabilities());

    /// <summary>The eight platforms with their limits, and whether the Site admin has enabled each for the organization.</summary>
    [HttpGet("platform-rules")]
    [ProducesResponseType<IReadOnlyList<PlatformRuleResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlatformRuleResponse>>> PlatformRules(CancellationToken cancellationToken) =>
        Ok(await capabilities.GetPlatformRulesAsync(cancellationToken));
}
