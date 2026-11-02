using Microsoft.AspNetCore.Authorization;
using Platform.Helpers.Constants;

namespace Platform.Auth.Services;

/// <summary>
/// The authorization rules, registered by <c>AddAuthModule()</c>. Controllers name them through
/// <see cref="PolicyNames"/>; none of them check a role inline. Every rule except <c>SiteAdmin</c> asks
/// whether the access token carries a permission (see <see cref="PermissionAuthorizationHandler"/>); which
/// roles hold which permission is data the Site admin sets, not code.
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

        // One policy per permission, named PolicyNames.PermissionPrefix + the key, for endpoints that
        // need exactly that permission. A mistyped name fails the request loudly instead of allowing it.
        foreach (var key in PermissionKeys.All)
        {
            options.AddPolicy(PolicyNames.PermissionPrefix + key, policy => policy
                .Combine(signedIn)
                .AddRequirements(new PermissionRequirement(key)));
        }

        // The three general rules keep their names and now stand for a baseline permission each.
        options.AddPolicy(PolicyNames.CanRead, policy => policy
            .Combine(signedIn)
            .AddRequirements(new PermissionRequirement(PermissionKeys.DataRead)));

        options.AddPolicy(PolicyNames.CanWrite, policy => policy
            .Combine(signedIn)
            .AddRequirements(new PermissionRequirement(PermissionKeys.DataWrite)));

        options.AddPolicy(PolicyNames.CanManage, policy => policy
            .Combine(signedIn)
            .AddRequirements(new PermissionRequirement(PermissionKeys.OrganizationManage)));

        // A site admin token has no agency, so it is built apart from signedIn on purpose. Naming this
        // policy on an endpoint replaces the fallback, which is how those endpoints take such a token.
        options.AddPolicy(PolicyNames.SiteAdmin, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.UserId)
            .RequireRole(RoleNames.SiteAdmin));
    }
}
