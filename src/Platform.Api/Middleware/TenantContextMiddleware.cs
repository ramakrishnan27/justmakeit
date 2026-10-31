using System.Security.Claims;
using Platform.Helpers.Constants;
using Platform.Helpers.Tenancy;

namespace Platform.Api.Middleware;

/// <summary>
/// Copies the agency, user and role from the validated access token into the scoped
/// <see cref="TenantContext"/>, which the connection factory hands to the database. Without a valid
/// token the context stays empty and row-level security matches nothing.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            tenant.Set(
                ParseGuid(user.FindFirstValue(ClaimNames.AgencyId)),
                ParseGuid(user.FindFirstValue(ClaimNames.UserId)),
                user.FindFirstValue(ClaimNames.Role));
        }

        return next(context);
    }

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
