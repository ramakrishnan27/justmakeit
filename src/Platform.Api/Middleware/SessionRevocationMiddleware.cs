using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Api.Middleware;

/// <summary>
/// Refuses a member's access token that was issued before that person's access to the organization was changed or
/// ended (a new role, or removal from the team), with 401 <c>session_ended</c>. A token is the whole session and lasts
/// up to 30 days, so this is the only way to take one back. The web client treats any 401 as the end of the session.
/// <para>
/// The check is two ids and a number against a list the API holds in memory (<see cref="ISessionRevocationService"/>),
/// so it costs no database read per request. A token with no readable <c>iat</c> cannot be compared and is refused.
/// Site admin tokens are skipped (a different session, checked in the database on every call), and so are endpoints
/// marked <c>[AllowAnonymous]</c> (sign-in must work with whatever token the browser still holds). If the list cannot
/// be read the exception is not caught: the request fails (500) and is never let through.
/// </para>
/// </summary>
public sealed class SessionRevocationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISessionRevocationService revocations)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.FindFirstValue(ClaimNames.Role) != RoleNames.SiteAdmin
            && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && Guid.TryParse(user.FindFirstValue(ClaimNames.AgencyId), out var agencyId)
            && Guid.TryParse(user.FindFirstValue(ClaimNames.UserId), out var userId))
        {
            // A token without a member's ids is judged by the authorization policies, as before.
            var hasIssuedAt = long.TryParse(
                user.FindFirstValue(ClaimNames.IssuedAt), NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAt);

            if (!hasIssuedAt || await revocations.IsRevokedAsync(agencyId, userId, issuedAt, context.RequestAborted))
            {
                throw new UnauthorizedException("Your access to this organization changed. Sign in again.", "session_ended");
            }
        }

        await next(context);
    }
}
