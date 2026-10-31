using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;

namespace Platform.Auth.Controllers;

/// <summary>
/// Sign-up, sign-in and session endpoints. Responses are marked no-store (errors too, by the exception
/// middleware), and every route is rate limited: register and login tightly, the rest more loosely.
/// </summary>
[ApiController]
[Route("v1/auth")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Creates a user, a trial agency, an owner membership and a first workspace, and signs the user in.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.RegisterAsync(request, cancellationToken), StatusCodes.Status201Created);
    }

    /// <summary>Signs in with email and password, to the requested agency or the most recently used one.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.LoginAsync(request, cancellationToken), StatusCodes.Status200OK);
    }

    /// <summary>Exchanges the refresh cookie for a new access token and a new refresh cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken) =>
        Issue(await auth.RefreshAsync(RefreshTokenCookie.Read(Request), cancellationToken), StatusCodes.Status200OK);

    /// <summary>Revokes the refresh token and removes the cookie. Safe to call without a session.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Issues tokens for another agency the signed-in user belongs to.</summary>
    [HttpPost("switch-agency")]
    [Authorize]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SwitchAgency(SwitchAgencyRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var result = await auth.SwitchAgencyAsync(request, RefreshTokenCookie.Read(Request), cancellationToken);
        return Issue(result, StatusCodes.Status200OK);
    }

    /// <summary>The signed-in user, the current agency and role, and every membership.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken) =>
        await auth.GetMeAsync(cancellationToken);

    // The refresh token leaves only through the cookie; the body carries the access token alone.
    private ObjectResult Issue(AuthResult result, int statusCode)
    {
        RefreshTokenCookie.Write(Response, result.RefreshToken, result.RefreshTokenExpiresAt);
        return StatusCode(statusCode, result.Tokens);
    }
}
