using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Platform.Auth.DTOs;
using Platform.Auth.Services.Google;
using Platform.Auth.Settings;

namespace Platform.Auth.Controllers;

/// <summary>
/// Sign-in and registration with Google, as plain browser navigations: the page links to <c>start</c>, the
/// server sends the browser to Google, and Google sends it back to <c>callback</c>, which signs the person in
/// and sends them on to the app. There is no script from Google on our pages and no token in any address.
/// Failures end as a redirect back to the form with an error code, never as a JSON error, because the person is
/// looking at a page, not at an API client.
/// </summary>
[ApiController]
[Route("v1/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class GoogleAuthController(IGoogleSignInService google) : ControllerBase
{
    public const string AppHome = "/dashboard";
    public const string LoginPage = "/login";
    public const string RegisterPage = "/register";

    private const string CallbackPath = "/v1/auth/google/callback";

    // Lax, not Strict: Google returns the browser with a top-level GET from another site, and a Strict cookie
    // would not be sent on that request. It is scoped to the Google paths, HttpOnly, Secure and short-lived.
    private const string StateCookieName = "google_oauth";
    private const string StateCookiePath = "/v1/auth/google";

    /// <summary>Which sign-in methods this server offers, so the pages show only buttons that work.</summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    [ProducesResponseType<ProvidersResponse>(StatusCodes.Status200OK)]
    public ActionResult<ProvidersResponse> Providers() => new ProvidersResponse(google.Enabled);

    /// <summary>Sends the browser to Google. <c>intent</c> is login or register; register also needs businessModel and organizationName.</summary>
    [HttpGet("google/start")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Start([FromQuery] GoogleStartRequest request)
    {
        var page = request.IsRegistration ? RegisterPage : LoginPage;
        if (!google.Enabled)
        {
            return ToPage(page, GoogleSignInErrors.Unavailable);
        }

        if (request.Validate().Any)
        {
            return ToPage(page, GoogleSignInErrors.InvalidRequest);
        }

        var start = google.Begin(request, $"{Request.Scheme}://{Request.Host}{CallbackPath}");
        Response.Cookies.Append(StateCookieName, start.StateCookieValue, StateCookieOptions(start.ExpiresAt));
        return Redirect(start.AuthorizationUrl);
    }

    /// <summary>Where Google sends the person back. Signs them in and redirects to the app, or back to the form with an error code.</summary>
    [HttpGet("google/callback")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Credentials)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        // The address carries a one-time code. Do not pass it on to any page we send the browser to next.
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var stateCookie = Request.Cookies[StateCookieName];
        Response.Cookies.Delete(StateCookieName, StateCookieOptions(null));

        var outcome = await google.CompleteAsync(new GoogleCallback(code, state, error), stateCookie, cancellationToken);
        if (outcome.Session is { } session)
        {
            RefreshTokenCookie.Agency.Write(Response, session.RefreshToken, session.RefreshTokenExpiresAt);
            return Redirect(AppHome);
        }

        return ToPage(outcome.Intent == GoogleStartRequest.Register ? RegisterPage : LoginPage, outcome.ErrorCode!);
    }

    private RedirectResult ToPage(string page, string errorCode) => Redirect($"{page}?error={Uri.EscapeDataString(errorCode)}");

    private static CookieOptions StateCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = StateCookiePath,
        IsEssential = true,
        Expires = expires
    };
}
