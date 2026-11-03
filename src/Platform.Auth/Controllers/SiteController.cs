using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;
using Platform.Helpers.Results;

namespace Platform.Auth.Controllers;

/// <summary>
/// The site admin console API. Sign-in sets its own cookie (<c>site_access_token</c>, path /v1/site) holding a token
/// that carries the user and the <c>siteadmin</c> role but no agency; there is no refresh. Every route is
/// rate limited, responses are marked no-store, and the data routes need the <see cref="PolicyNames.SiteAdmin"/> policy.
/// </summary>
[ApiController]
[Route("v1/site")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class SiteController(ISiteAdminAuthService auth, ISiteAdminDashboardService dashboard, IMenuService menus) : ControllerBase
{
    /// <summary>Signs a site admin in. Anyone else gets the same 401 as a wrong password.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(SiteLoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.LoginAsync(request, cancellationToken));
    }

    /// <summary>Signs out of this browser by removing the cookie. Safe to call without a session.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        AccessTokenCookie.Site.Clear(Response);
        return NoContent();
    }

    /// <summary>The signed-in site admin, and when the sign-in ends.</summary>
    [HttpGet("me")]
    [Authorize(Policy = PolicyNames.SiteAdmin)]
    [ProducesResponseType<SiteMeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SiteMeResponse>> Me(CancellationToken cancellationToken) =>
        (await auth.GetMeAsync(cancellationToken)) with { SessionExpiresAt = User.SessionExpiresAt() };

    /// <summary>The site admin sidebar: the visible items of the admin menu. Read from memory, like the member menu.</summary>
    [HttpGet("menu")]
    [Authorize(Policy = PolicyNames.SiteAdmin)]
    [ProducesResponseType<MenuResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuResponse>> Menu(CancellationToken cancellationToken) =>
        await menus.GetSiteAdminMenuAsync(cancellationToken);

    /// <summary>Platform-wide totals: agencies by plan, users, workspaces and memberships.</summary>
    [HttpGet("overview")]
    [Authorize(Policy = PolicyNames.SiteAdmin)]
    [ProducesResponseType<SiteOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SiteOverviewResponse>> Overview(CancellationToken cancellationToken) =>
        await dashboard.GetOverviewAsync(cancellationToken);

    /// <summary>One page of every agency on the platform, newest first.</summary>
    [HttpGet("agencies")]
    [Authorize(Policy = PolicyNames.SiteAdmin)]
    [ProducesResponseType<PagedResult<SiteAgencyResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SiteAgencyResponse>>> Agencies(
        [FromQuery] SiteAgenciesQuery query, CancellationToken cancellationToken)
    {
        query.Validate().ThrowIfAny();
        return await dashboard.ListAgenciesAsync(query, cancellationToken);
    }

    // The token leaves only through the cookie; the body says when it ends.
    private OkObjectResult Issue(AuthResult result)
    {
        AccessTokenCookie.Site.Write(Response, result.AccessToken, result.ExpiresAt);
        return Ok(new SessionResponse(result.ExpiresAt));
    }
}
