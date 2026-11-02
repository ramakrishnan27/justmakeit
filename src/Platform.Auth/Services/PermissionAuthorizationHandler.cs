using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Platform.Helpers.Constants;

namespace Platform.Auth.Services;

/// <summary>
/// Decides from the access token alone, with no database and no other service, so checking a permission
/// costs a few claim reads on every request. It succeeds only when the token names a user and an
/// organization, carries a role this build knows, and lists the permission the endpoint asks for. It never
/// fails a requirement outright, so another handler could still allow it, and anything missing is a denial.
/// <para>
/// Whether the token is out of date (the Site admin changed a grant after it was issued) is not decided
/// here: <c>PermissionVersionMiddleware</c> answers such a token 401 before authorization runs.
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;

        if (Guid.TryParse(user.FindFirstValue(ClaimNames.UserId), out _)
            && Guid.TryParse(user.FindFirstValue(ClaimNames.AgencyId), out _)
            && RolePermissions.TryParse(user.FindFirstValue(ClaimNames.Role), out _)
            && user.HasClaim(ClaimNames.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
