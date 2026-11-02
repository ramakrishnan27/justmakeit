using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Tests;

/// <summary>
/// The authorization rules, evaluated by the real authorization service with the policies and the handler
/// the API registers. A member's rights are the permissions in the token; these tests build tokens the way
/// the issuer does, with the permissions a role holds by default unless a test says otherwise.
/// </summary>
public sealed class AuthPoliciesTests
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddLogging()
        .AddAuthorization(AuthPolicies.Configure)
        .AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>()
        .BuildServiceProvider();

    /// <summary>What the issuer puts in a role's token in a fresh installation.</summary>
    private static string[] DefaultPermissions(string? role) =>
        RolePermissions.TryParse(role, out var parsed)
            ? PermissionKeys.All.Where(key => parsed.GrantedByDefault(key)).ToArray()
            : PermissionKeys.All.ToArray(); // a role this build does not know: every key, to show the role check matters

    private static ClaimsPrincipal SignedIn(
        string? role, bool withAgency = true, bool withUser = true, IEnumerable<string>? permissions = null)
    {
        var claims = new List<Claim>();
        if (withUser)
        {
            claims.Add(new Claim(ClaimNames.UserId, Guid.NewGuid().ToString()));
        }

        if (withAgency)
        {
            claims.Add(new Claim(ClaimNames.AgencyId, Guid.NewGuid().ToString()));
        }

        if (role is not null)
        {
            claims.Add(new Claim(ClaimNames.Role, role));
        }

        claims.AddRange((permissions ?? DefaultPermissions(role)).Select(key => new Claim(ClaimNames.Permission, key)));
        claims.Add(new Claim(ClaimNames.PermissionVersion, "1"));

        // The same claim types the JWT scheme is configured with.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimNames.UserId, ClaimNames.Role));
    }

    private static ClaimsPrincipal SiteAdminToken(bool withUser = true, string role = "siteadmin")
    {
        // What the site admin login issues: the user and the role, and no agency.
        var claims = new List<Claim> { new(ClaimNames.Role, role) };
        if (withUser)
        {
            claims.Add(new Claim(ClaimNames.UserId, Guid.NewGuid().ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimNames.UserId, ClaimNames.Role));
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private async Task<bool> AllowedAsync(ClaimsPrincipal user, string policy) =>
        (await _provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy)).Succeeded;

    [Theory]
    [InlineData("organizationadmin", PolicyNames.CanRead, true)]
    [InlineData("clientmanager", PolicyNames.CanRead, true)]
    [InlineData("marketingmanager", PolicyNames.CanRead, true)]
    [InlineData("contentwriter", PolicyNames.CanRead, true)]
    [InlineData("organizationadmin", PolicyNames.CanWrite, true)]
    [InlineData("clientmanager", PolicyNames.CanWrite, false)]
    [InlineData("marketingmanager", PolicyNames.CanWrite, true)]
    [InlineData("contentwriter", PolicyNames.CanWrite, true)]
    [InlineData("organizationadmin", PolicyNames.CanManage, true)]
    [InlineData("clientmanager", PolicyNames.CanManage, false)]
    [InlineData("marketingmanager", PolicyNames.CanManage, false)]
    [InlineData("contentwriter", PolicyNames.CanManage, false)]
    public async Task EachRole_GetsExactlyItsPolicies(string role, string policy, bool expected)
    {
        Assert.Equal(expected, await AllowedAsync(SignedIn(role), policy));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead)]
    [InlineData(PolicyNames.CanWrite)]
    [InlineData(PolicyNames.CanManage)]
    public async Task AnAnonymousCaller_GetsNoPolicy(string policy)
    {
        Assert.False(await AllowedAsync(Anonymous(), policy));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead)]
    [InlineData(PolicyNames.CanWrite)]
    [InlineData(PolicyNames.CanManage)]
    public async Task ATokenWithoutARole_GetsNoPolicy(string policy)
    {
        Assert.False(await AllowedAsync(SignedIn(role: null), policy));
    }

    [Theory]
    [InlineData("OrganizationAdmin")]
    [InlineData("organizationAdmin")]
    [InlineData("ORGANIZATIONADMIN")]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("viewer")]
    [InlineData("superuser")]
    [InlineData("0")]
    [InlineData("")]
    public async Task AnUnknownMiscasedOrRetiredRole_GetsNoPolicy(string role)
    {
        Assert.False(await AllowedAsync(SignedIn(role), PolicyNames.CanRead));
        Assert.False(await AllowedAsync(SignedIn(role), PolicyNames.CanManage));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead)]
    [InlineData(PolicyNames.CanManage)]
    public async Task ATokenWithoutAnAgency_GetsNoPolicy_EvenForAnOrganizationAdmin(string policy)
    {
        Assert.False(await AllowedAsync(SignedIn("organizationadmin", withAgency: false), policy));
    }

    [Fact]
    public async Task ATokenWithoutAUser_GetsNoPolicy_EvenForAnOrganizationAdmin()
    {
        Assert.False(await AllowedAsync(SignedIn("organizationadmin", withUser: false), PolicyNames.CanManage));
    }

    [Fact]
    public async Task TheFallbackPolicy_RequiresASignedInUserWithAnAgency()
    {
        var fallback = await _provider.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        var authorization = _provider.GetRequiredService<IAuthorizationService>();

        Assert.NotNull(fallback);
        Assert.True((await authorization.AuthorizeAsync(SignedIn("clientmanager"), null, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Anonymous(), null, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SignedIn("clientmanager", withAgency: false), null, fallback)).Succeeded);
    }

    // --- Permissions -------------------------------------------------------------------------

    public static TheoryData<string, string> EveryRoleAndEveryPermission()
    {
        var data = new TheoryData<string, string>();
        foreach (var role in Enum.GetValues<OrganizationRole>())
        {
            foreach (var key in PermissionKeys.All)
            {
                data.Add(role.ToDb(), key);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRoleAndEveryPermission))]
    public async Task APermissionPolicy_AllowsExactlyTheRolesThatHoldItByDefault(string role, string key)
    {
        Assert.Equal(
            RolePermissions.TryParse(role, out var parsed) && parsed.GrantedByDefault(key),
            await AllowedAsync(SignedIn(role), PolicyNames.PermissionPrefix + key));
    }

    [Fact]
    public async Task EveryPermissionInTheCatalog_HasAPolicy()
    {
        var policies = _provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var key in PermissionKeys.All)
        {
            Assert.NotNull(await policies.GetPolicyAsync(PolicyNames.PermissionPrefix + key));
        }
    }

    [Fact]
    public async Task APolicyNamedForAPermissionThatDoesNotExist_FailsLoudly_InsteadOfAllowingOrDenying()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AllowedAsync(SignedIn("organizationadmin"), PolicyNames.PermissionPrefix + "workspaces.crate"));
    }

    [Fact]
    public async Task ARevokedPermission_IsDenied_EvenToTheRoleThatHadItByDefault()
    {
        // What the Site admin's change looks like in a token issued afterwards.
        var withoutCreate = PermissionKeys.All.Where(key => key != PermissionKeys.WorkspacesCreate);
        var token = SignedIn("organizationadmin", permissions: withoutCreate);

        Assert.False(await AllowedAsync(token, PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesCreate));
        Assert.True(await AllowedAsync(token, PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesView));
        Assert.True(await AllowedAsync(token, PolicyNames.CanManage)); // organization.manage was not revoked
    }

    [Fact]
    public async Task AGrantedPermission_IsAllowed_ToARoleThatDidNotHaveItByDefault()
    {
        var token = SignedIn("contentwriter", permissions: [PermissionKeys.DashboardView, PermissionKeys.WorkspacesCreate]);

        Assert.True(await AllowedAsync(token, PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesCreate));
        Assert.False(await AllowedAsync(token, PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesView));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead, PermissionKeys.DataRead)]
    [InlineData(PolicyNames.CanWrite, PermissionKeys.DataWrite)]
    [InlineData(PolicyNames.CanManage, PermissionKeys.OrganizationManage)]
    public async Task TheGeneralPolicies_StandForTheirBaselinePermission(string policy, string baseline)
    {
        Assert.True(await AllowedAsync(SignedIn("clientmanager", permissions: [baseline]), policy));
        Assert.False(await AllowedAsync(
            SignedIn("organizationadmin", permissions: PermissionKeys.All.Where(key => key != baseline)), policy));
    }

    [Fact]
    public async Task ATokenWithAValidRoleButNoPermissions_PassesNoPermissionPolicy()
    {
        var token = SignedIn("organizationadmin", permissions: []);

        foreach (var key in PermissionKeys.All)
        {
            Assert.False(await AllowedAsync(token, PolicyNames.PermissionPrefix + key));
        }

        Assert.False(await AllowedAsync(token, PolicyNames.CanRead));
    }

    [Theory]
    [InlineData("Workspaces.View")]
    [InlineData("workspaces.view ")]
    [InlineData("workspaces.*")]
    [InlineData("*")]
    [InlineData("")]
    public async Task APermissionClaim_MustMatchTheKeyExactly(string claimValue)
    {
        var token = SignedIn("organizationadmin", permissions: [claimValue]);

        Assert.False(await AllowedAsync(token, PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesView));
    }

    [Fact]
    public async Task APermissionAlone_IsNotEnough_WithoutAKnownRole()
    {
        Assert.False(await AllowedAsync(SignedIn(role: null, permissions: PermissionKeys.All), PolicyNames.PermissionPrefix + PermissionKeys.DashboardView));
        Assert.False(await AllowedAsync(SignedIn("owner", permissions: PermissionKeys.All), PolicyNames.PermissionPrefix + PermissionKeys.DashboardView));
    }

    [Fact]
    public async Task APermissionAlone_IsNotEnough_WithoutAnAgencyOrAUser()
    {
        var policy = PolicyNames.PermissionPrefix + PermissionKeys.DashboardView;

        Assert.False(await AllowedAsync(SignedIn("organizationadmin", withAgency: false), policy));
        Assert.False(await AllowedAsync(SignedIn("organizationadmin", withUser: false), policy));
    }

    [Fact]
    public async Task ASiteAdminToken_PassesNoPermissionPolicy_EvenIfItWereToCarryTheClaims()
    {
        var siteAdmin = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimNames.UserId, Guid.NewGuid().ToString()),
                new Claim(ClaimNames.Role, RoleNames.SiteAdmin),
                .. PermissionKeys.All.Select(key => new Claim(ClaimNames.Permission, key))
            ],
            "test", ClaimNames.UserId, ClaimNames.Role));

        foreach (var key in PermissionKeys.All)
        {
            Assert.False(await AllowedAsync(siteAdmin, PolicyNames.PermissionPrefix + key));
        }
    }

    // --- Site admin --------------------------------------------------------------------------

    [Fact]
    public async Task ASiteAdminToken_PassesTheSiteAdminPolicy_WithoutAnAgency()
    {
        Assert.True(await AllowedAsync(SiteAdminToken(), PolicyNames.SiteAdmin));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead)]
    [InlineData(PolicyNames.CanWrite)]
    [InlineData(PolicyNames.CanManage)]
    public async Task ASiteAdminToken_PassesNoAgencyPolicy(string policy)
    {
        Assert.False(await AllowedAsync(SiteAdminToken(), policy));
    }

    [Fact]
    public async Task ASiteAdminToken_FailsTheFallbackAndDefaultPolicies_SoNoUnmarkedEndpointTakesIt()
    {
        var authorization = _provider.GetRequiredService<IAuthorizationService>();
        var provider = _provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.False((await authorization.AuthorizeAsync(SiteAdminToken(), null, (await provider.GetFallbackPolicyAsync())!)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SiteAdminToken(), null, await provider.GetDefaultPolicyAsync())).Succeeded);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("clientmanager")]
    [InlineData("marketingmanager")]
    [InlineData("contentwriter")]
    public async Task EveryOrganizationRole_FailsTheSiteAdminPolicy_EvenWithAnAgency(string role)
    {
        Assert.False(await AllowedAsync(SignedIn(role), PolicyNames.SiteAdmin));
    }

    [Fact]
    public async Task AnAnonymousCaller_FailsTheSiteAdminPolicy()
    {
        Assert.False(await AllowedAsync(Anonymous(), PolicyNames.SiteAdmin));
    }

    [Fact]
    public async Task ASiteAdminTokenWithoutAUser_FailsTheSiteAdminPolicy()
    {
        Assert.False(await AllowedAsync(SiteAdminToken(withUser: false), PolicyNames.SiteAdmin));
    }

    [Theory]
    [InlineData("SiteAdmin")]
    [InlineData("SITEADMIN")]
    [InlineData("site_admin")]
    [InlineData("admin")]
    [InlineData("")]
    public async Task AMiscasedOrInventedRole_FailsTheSiteAdminPolicy(string role)
    {
        Assert.False(await AllowedAsync(SiteAdminToken(role: role), PolicyNames.SiteAdmin));
    }

    [Fact]
    public async Task TheDefaultPolicy_MatchesTheFallback()
    {
        var provider = _provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = _provider.GetRequiredService<IAuthorizationService>();
        var defaultPolicy = await provider.GetDefaultPolicyAsync();

        Assert.False((await authorization.AuthorizeAsync(Anonymous(), null, defaultPolicy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SignedIn("clientmanager", withAgency: false), null, defaultPolicy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(SignedIn("clientmanager"), null, defaultPolicy)).Succeeded);
    }
}
