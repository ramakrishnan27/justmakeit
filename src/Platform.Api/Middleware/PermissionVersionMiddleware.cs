using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Api.Middleware;

/// <summary>
/// Refuses an organization member's access token that was issued before the Site admin last changed a
/// permission, with 401 <c>permissions_changed</c>. There is no refresh: the web client treats any 401 as the end of the
/// session and sends the person to the sign-in page, and the token they get there carries the current permissions.
/// The comparison is one number from <see cref="IPermissionService"/>, which keeps the current version in memory,
/// so this costs no database read per request. A token with no version (one issued before permissions travelled in
/// the token) is out of date too.
/// <para>
/// Site admin tokens carry no permissions and are skipped, and so are endpoints marked
/// <c>[AllowAnonymous]</c> (sign-in must work with whatever token the browser still holds).
/// It runs before authorization, so an out-of-date token is told to sign in again (401) instead of being
/// denied (403) for permissions it was never meant to be judged by. If the current version cannot be
/// read the exception is not caught: the request fails (500) and is never let through.
/// </para>
/// </summary>
public sealed class PermissionVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPermissionService permissions)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.FindFirstValue(ClaimNames.Role) != RoleNames.SiteAdmin
            && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            var current = await permissions.GetVersionAsync(context.RequestAborted);
            var presented = long.TryParse(user.FindFirstValue(ClaimNames.PermissionVersion), out var version) ? version : (long?)null;

            if (presented != current)
            {
                throw new UnauthorizedException("Your permissions changed. Sign in again.", "permissions_changed");
            }
        }

        await next(context);
    }
}
