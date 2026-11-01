using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>The access-token rules, run through the real authentication scheme the API registers.</summary>
public sealed class JwtAuthenticationTests : IDisposable
{
    private const string Issuer = "platform";
    private const string Audience = "platform-web";

    // 64 bytes so an HS512 token can be signed with the very same key: the algorithm alone must get it rejected.
    private readonly string _key = new('k', 64);
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly ApiHostFactory _factory;

    public JwtAuthenticationTests()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Jwt:SigningKey"] = _key;
        _factory = new ApiHostFactory(settings);
    }

    public void Dispose() => _factory.Dispose();

    private string Mint(
        string? key = null,
        string issuer = Issuer,
        string audience = Audience,
        TimeSpan? lifetime = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var now = DateTime.UtcNow;
        var lifespan = lifetime ?? TimeSpan.FromMinutes(15);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = lifespan > TimeSpan.Zero ? now : now + lifespan - TimeSpan.FromMinutes(1),
            Expires = now + lifespan,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.UserId] = _userId.ToString(),
                [ClaimNames.AgencyId] = _agencyId.ToString(),
                [ClaimNames.Role] = "organizationadmin"
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? _key)), algorithm)
        });
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string? token)
    {
        using var scope = _factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (token is not null)
        {
            context.Request.Headers.Authorization = "Bearer " + token;
        }

        return await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(context, "Bearer");
    }

    [Fact]
    public async Task ATokenSignedWithTheConfiguredKey_IsAccepted_WithItsClaimsKeptByName()
    {
        var result = await AuthenticateAsync(Mint());

        Assert.True(result.Succeeded, result.Failure?.Message);
        var principal = result.Principal!;
        Assert.Equal(_userId.ToString(), principal.FindFirstValue(ClaimNames.UserId));
        Assert.Equal(_agencyId.ToString(), principal.FindFirstValue(ClaimNames.AgencyId));
        Assert.Equal("organizationadmin", principal.FindFirstValue(ClaimNames.Role));
        Assert.True(principal.IsInRole("organizationadmin"));
    }

    [Fact]
    public async Task NoToken_IsNotAuthenticated()
    {
        Assert.False((await AuthenticateAsync(null)).Succeeded);
    }

    [Fact]
    public async Task GarbageToken_IsRejected()
    {
        Assert.False((await AuthenticateAsync("not.a.jwt")).Succeeded);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected()
    {
        Assert.False((await AuthenticateAsync(Mint(key: new string('x', 64)))).Succeeded);
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected()
    {
        Assert.False((await AuthenticateAsync(Mint(lifetime: TimeSpan.FromMinutes(-10)))).Succeeded);
    }

    [Fact]
    public async Task ATokenForAnotherAudience_IsRejected()
    {
        Assert.False((await AuthenticateAsync(Mint(audience: "somebody-else"))).Succeeded);
    }

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRejected()
    {
        Assert.False((await AuthenticateAsync(Mint(issuer: "somebody-else"))).Succeeded);
    }

    [Fact]
    public async Task ATokenUsingAnotherAlgorithm_IsRejected()
    {
        Assert.False((await AuthenticateAsync(Mint(algorithm: SecurityAlgorithms.HmacSha512))).Succeeded);
    }

    [Fact]
    public async Task AnUnsignedToken_IsRejected()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(
            $$"""{"iss":"{{Issuer}}","aud":"{{Audience}}","user_id":"{{_userId}}","agency_id":"{{_agencyId}}","role":"organizationadmin","exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}""");

        Assert.False((await AuthenticateAsync(unsigned)).Succeeded);
    }
}
