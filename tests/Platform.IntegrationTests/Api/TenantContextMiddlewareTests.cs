using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Platform.Api.Middleware;
using Platform.Helpers.Constants;
using Platform.Helpers.Tenancy;

namespace Platform.IntegrationTests.Api;

public sealed class TenantContextMiddlewareTests
{
    private static async Task<(TenantContext Tenant, bool NextCalled)> RunAsync(ClaimsPrincipal? user)
    {
        var tenant = new TenantContext();
        var context = new DefaultHttpContext();
        if (user is not null)
        {
            context.User = user;
        }

        var nextCalled = false;
        await new TenantContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }).InvokeAsync(context, tenant);

        return (tenant, nextCalled);
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public async Task AuthenticatedUser_FillsTheTenantContextFromTheTokenClaims()
    {
        var agency = Guid.NewGuid();
        var user = Guid.NewGuid();

        var (tenant, nextCalled) = await RunAsync(Authenticated(
            new Claim(ClaimNames.AgencyId, agency.ToString()),
            new Claim(ClaimNames.UserId, user.ToString()),
            new Claim(ClaimNames.Role, "marketingmanager")));

        Assert.True(nextCalled);
        Assert.Equal(agency, tenant.AgencyId);
        Assert.Equal(user, tenant.UserId);
        Assert.Equal("marketingmanager", tenant.Role);
    }

    [Fact]
    public async Task AnonymousRequest_LeavesTheContextEmpty()
    {
        var (tenant, nextCalled) = await RunAsync(null);

        Assert.True(nextCalled);
        Assert.Null(tenant.AgencyId);
        Assert.Null(tenant.UserId);
        Assert.Null(tenant.Role);
    }

    [Fact]
    public async Task ClaimsOnAnUnauthenticatedPrincipal_AreIgnored()
    {
        // An identity without an authentication type is not authenticated, whatever claims it carries.
        var spoofed = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.AgencyId, Guid.NewGuid().ToString())]));

        var (tenant, _) = await RunAsync(spoofed);

        Assert.Null(tenant.AgencyId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    public async Task MalformedIds_BecomeNullInsteadOfThrowing(string value)
    {
        var (tenant, nextCalled) = await RunAsync(Authenticated(
            new Claim(ClaimNames.AgencyId, value),
            new Claim(ClaimNames.UserId, value)));

        Assert.True(nextCalled);
        Assert.Null(tenant.AgencyId);
        Assert.Null(tenant.UserId);
    }

    [Fact]
    public async Task MissingAgencyClaim_LeavesOnlyTheUserSet()
    {
        var user = Guid.NewGuid();

        var (tenant, _) = await RunAsync(Authenticated(new Claim(ClaimNames.UserId, user.ToString())));

        Assert.Null(tenant.AgencyId);
        Assert.Equal(user, tenant.UserId);
    }
}
