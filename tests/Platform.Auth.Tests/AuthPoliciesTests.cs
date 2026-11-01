using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

/// <summary>The role rules, evaluated by the real authorization service with the policies the API registers.</summary>
public sealed class AuthPoliciesTests
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddLogging()
        .AddAuthorization(AuthPolicies.Configure)
        .BuildServiceProvider();

    private static ClaimsPrincipal SignedIn(string? role, bool withAgency = true, bool withUser = true)
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
