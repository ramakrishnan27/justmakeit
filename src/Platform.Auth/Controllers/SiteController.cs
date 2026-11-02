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
/// The site admin console API. Sign-in and sessions use their own cookie (<c>site_refresh_token</c>, path
/// /v1/site) and a token that carries the user and the <c>siteadmin</c> role but no agency. Every route is
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
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(SiteLoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.LoginAsync(request, cancellationToken));
    }

    /// <summary>Exchanges the site refresh cookie for a new access token and a new cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken) =>
        Issue(await auth.RefreshAsync(RefreshTokenCookie.Site.Read(Request), cancellationToken));

    /// <summary>Revokes the site refresh token and removes the cookie. Safe to call without a session.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(RefreshTokenCookie.Site.Read(Request), cancellationToken);
        RefreshTokenCookie.Site.Clear(Response);
        return NoContent();
    }

    /// <summary>The signed-in site admin.</summary>
    [HttpGet("me")]
    [Authorize(Policy = PolicyNames.SiteAdmin)]
    [ProducesResponseType<SiteMeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SiteMeResponse>> Me(CancellationToken cancellationToken) =>
        await auth.GetMeAsync(cancellationToken);

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

    // The refresh token leaves only through the cookie; the body carries the access token alone.
    private OkObjectResult Issue(AuthResult result)
    {
        RefreshTokenCookie.Site.Write(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
        return Ok(result.Tokens);
    }
}
