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
/// Sign-up, sign-in and session endpoints. Signing in sets the access token as an HttpOnly cookie (see
/// <see cref="AccessTokenCookie"/>) and answers with when it ends; there is no refresh, so an expired token means
/// signing in again. Responses are marked no-store (errors too, by the exception middleware), and every route is
/// rate limited: register and login tightly, the rest more loosely.
/// </summary>
[ApiController]
[Route("v1/auth")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>
    /// Creates a user, a trial organization of the chosen business model (company, agency or creator), the
    /// founder's membership and a first workspace, and signs the user in. A company or agency founder is an
    /// organization admin; a creator is a content writer.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.RegisterAsync(request, cancellationToken), StatusCodes.Status201Created);
    }

    /// <summary>Signs in with email and password, to the requested organization or the most recently used one.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.LoginAsync(request, cancellationToken), StatusCodes.Status200OK);
    }

    /// <summary>
    /// Signs out of this browser by removing the cookie. Safe to call without a session. Nothing is stored on the
    /// server, so a copy of the token made earlier stays valid until it expires.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        AccessTokenCookie.Member.Clear(Response);
        return NoContent();
    }

    /// <summary>Issues a new token, and replaces the cookie, for another agency the signed-in user belongs to.</summary>
    [HttpPost("switch-agency")]
    [Authorize]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SwitchAgency(SwitchAgencyRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.SwitchAgencyAsync(request, cancellationToken), StatusCodes.Status200OK);
    }

    /// <summary>
    /// What the invitation page shows for the secret in a link: the organization, the role and the invited address.
    /// The secret is sent in the body, never in an address. A link that never existed, was used, was revoked or ran out
    /// answers the same 404 (<c>invitation_invalid</c>).
    /// </summary>
    [HttpPost("invitations/preview")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<InvitationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InvitationPreviewResponse>> PreviewInvitation(InvitationTokenRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Ok(await auth.PreviewInvitationAsync(request, cancellationToken));
    }

    /// <summary>
    /// Accepts an invitation: the person gets an account with the invited address, the name and password they chose, and
    /// the invited role, and is signed in (the cookie is set; the body holds only when it ends).
    /// </summary>
    [HttpPost("invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.AcceptInvitationAsync(request, cancellationToken), StatusCodes.Status201Created);
    }

    /// <summary>
    /// The signed-in person changes their own password (the current one is asked for). Every other session of theirs ends;
    /// the answer sets a fresh cookie for this one. A wrong current password is a 400 field error, not a 401.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return Issue(await auth.ChangePasswordAsync(request, cancellationToken), StatusCodes.Status200OK);
    }

    /// <summary>The signed-in user, the current agency and role, every membership, and when the sign-in ends.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken) =>
        (await auth.GetMeAsync(cancellationToken)) with { SessionExpiresAt = User.SessionExpiresAt() };

    // The token leaves only through the cookie; the body says when it ends.
    private ObjectResult Issue(AuthResult result, int statusCode)
    {
        AccessTokenCookie.Member.Write(Response, result.AccessToken, result.ExpiresAt);
        return StatusCode(statusCode, new SessionResponse(result.ExpiresAt));
    }
}
