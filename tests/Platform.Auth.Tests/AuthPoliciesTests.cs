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

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private async Task<bool> AllowedAsync(ClaimsPrincipal user, string policy) =>
        (await _provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy)).Succeeded;

    [Theory]
    [InlineData("owner", PolicyNames.CanRead, true)]
    [InlineData("admin", PolicyNames.CanRead, true)]
    [InlineData("manager", PolicyNames.CanRead, true)]
    [InlineData("viewer", PolicyNames.CanRead, true)]
    [InlineData("owner", PolicyNames.CanWrite, true)]
    [InlineData("admin", PolicyNames.CanWrite, true)]
    [InlineData("manager", PolicyNames.CanWrite, true)]
    [InlineData("viewer", PolicyNames.CanWrite, false)]
    [InlineData("owner", PolicyNames.CanManage, true)]
    [InlineData("admin", PolicyNames.CanManage, true)]
    [InlineData("manager", PolicyNames.CanManage, false)]
    [InlineData("viewer", PolicyNames.CanManage, false)]
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
    [InlineData("Owner")]
    [InlineData("OWNER")]
    [InlineData("superuser")]
    [InlineData("0")]
    [InlineData("")]
    public async Task AnUnknownOrMiscasedRole_GetsNoPolicy(string role)
    {
        Assert.False(await AllowedAsync(SignedIn(role), PolicyNames.CanRead));
        Assert.False(await AllowedAsync(SignedIn(role), PolicyNames.CanManage));
    }

    [Theory]
    [InlineData(PolicyNames.CanRead)]
    [InlineData(PolicyNames.CanManage)]
    public async Task ATokenWithoutAnAgency_GetsNoPolicy_EvenForAnOwner(string policy)
    {
        Assert.False(await AllowedAsync(SignedIn("owner", withAgency: false), policy));
    }

    [Fact]
    public async Task ATokenWithoutAUser_GetsNoPolicy_EvenForAnOwner()
    {
        Assert.False(await AllowedAsync(SignedIn("owner", withUser: false), PolicyNames.CanManage));
    }

    [Fact]
    public async Task TheFallbackPolicy_RequiresASignedInUserWithAnAgency()
    {
        var fallback = await _provider.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        var authorization = _provider.GetRequiredService<IAuthorizationService>();

        Assert.NotNull(fallback);
        Assert.True((await authorization.AuthorizeAsync(SignedIn("viewer"), null, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Anonymous(), null, fallback)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SignedIn("viewer", withAgency: false), null, fallback)).Succeeded);
    }

    [Fact]
    public async Task TheDefaultPolicy_MatchesTheFallback()
    {
        var provider = _provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = _provider.GetRequiredService<IAuthorizationService>();
        var defaultPolicy = await provider.GetDefaultPolicyAsync();

        Assert.False((await authorization.AuthorizeAsync(Anonymous(), null, defaultPolicy)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(SignedIn("viewer", withAgency: false), null, defaultPolicy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(SignedIn("viewer"), null, defaultPolicy)).Succeeded);
    }
}
