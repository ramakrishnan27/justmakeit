using Microsoft.AspNetCore.Authorization;
using Platform.Helpers.Constants;

namespace Platform.Auth.Services;

/// <summary>
/// The authorization rules, registered by <c>AddAuthModule()</c>. Controllers name them through
/// <see cref="PolicyNames"/>; none of them check a role inline.
/// </summary>
public static class AuthPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        var signedIn = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.UserId)
            .RequireClaim(ClaimNames.AgencyId)
            .Build();

        // Closed by default: an endpoint with no attribute at all still needs a signed-in user, and
        // the few public ones (register, login, refresh, logout, health) opt out with [AllowAnonymous].
        options.DefaultPolicy = signedIn;
        options.FallbackPolicy = signedIn;

        options.AddPolicy(PolicyNames.CanRead, policy => policy
            .Combine(signedIn)
            .RequireRole(RolePermissions.NamesWhere(RolePermissions.CanRead)));

        options.AddPolicy(PolicyNames.CanWrite, policy => policy
            .Combine(signedIn)
            .RequireRole(RolePermissions.NamesWhere(RolePermissions.CanWrite)));

        options.AddPolicy(PolicyNames.CanManage, policy => policy
            .Combine(signedIn)
            .RequireRole(RolePermissions.NamesWhere(RolePermissions.CanManage)));
    }
}
