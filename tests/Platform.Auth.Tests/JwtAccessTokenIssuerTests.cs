using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class JwtAccessTokenIssuerTests
{
    private readonly JwtSettings _settings = new() { SigningKey = new string('k', 48) };
    private readonly FixedTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();

    private JwtAccessTokenIssuer CreateIssuer() => new(Options.Create(_settings), _time);

    // The very rules every protected endpoint applies, so what is issued is what the API accepts.
    private async Task<Microsoft.IdentityModel.Tokens.TokenValidationResult> ValidateAsync(string token)
    {
        var options = new JwtBearerOptions();
        new ConfigureJwtBearerOptions(Options.Create(_settings)).Configure(JwtBearerDefaults.AuthenticationScheme, options);
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, options.TokenValidationParameters);
    }

    [Fact]
    public async Task Issue_ProducesATokenTheApiAccepts()
    {
        var token = CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Manager);

        var result = await ValidateAsync(token.Value);

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public void Issue_CarriesTheUserAgencyAndRoleClaims()
    {
        var token = new JsonWebToken(CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Admin).Value);

        Assert.Equal(_userId.ToString(), token.GetClaim(ClaimNames.UserId).Value);
        Assert.Equal(_agencyId.ToString(), token.GetClaim(ClaimNames.AgencyId).Value);
        Assert.Equal("admin", token.GetClaim(ClaimNames.Role).Value);
    }

    [Theory]
    [InlineData(AgencyRole.Owner, "owner")]
    [InlineData(AgencyRole.Admin, "admin")]
    [InlineData(AgencyRole.Manager, "manager")]
    [InlineData(AgencyRole.Viewer, "viewer")]
    public void Issue_WritesTheRoleAsItsLowerCaseName(AgencyRole role, string expected)
    {
        var token = new JsonWebToken(CreateIssuer().Issue(_userId, _agencyId, role).Value);

        Assert.Equal(expected, token.GetClaim(ClaimNames.Role).Value);
    }

    [Fact]
    public void Issue_SignsWithHs256()
    {
        Assert.Equal("HS256", new JsonWebToken(CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Owner).Value).Alg);
    }

    [Fact]
    public void Issue_ExpiresAfterTheConfiguredMinutes_FifteenByDefault()
    {
        var token = CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Owner);

        Assert.Equal(15, new JwtSettings().AccessTokenMinutes);
        Assert.Equal(_time.Now.AddMinutes(15), token.ExpiresAt);
        Assert.Equal(
            _time.Now.AddMinutes(15).ToUnixTimeSeconds(),
            new DateTimeOffset(new JsonWebToken(token.Value).ValidTo, TimeSpan.Zero).ToUnixTimeSeconds());
    }

    [Fact]
    public void Issue_HonoursACustomLifetime()
    {
        _settings.AccessTokenMinutes = 5;

        Assert.Equal(_time.Now.AddMinutes(5), CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Owner).ExpiresAt);
    }

    [Fact]
    public void Issue_UsesTheConfiguredIssuerAndAudience()
    {
        _settings.Issuer = "my-issuer";
        _settings.Audience = "my-audience";

        var token = new JsonWebToken(CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Owner).Value);

        Assert.Equal("my-issuer", token.Issuer);
        Assert.Contains("my-audience", token.Audiences);
    }

    [Fact]
    public void Issue_GivesEveryTokenItsOwnId()
    {
        var issuer = CreateIssuer();

        var first = new JsonWebToken(issuer.Issue(_userId, _agencyId, AgencyRole.Owner).Value);
        var second = new JsonWebToken(issuer.Issue(_userId, _agencyId, AgencyRole.Owner).Value);

        Assert.NotEqual(first.Id, second.Id);
    }

    // --- Site admin tokens -------------------------------------------------------------------

    [Fact]
    public void IssueSiteAdmin_CarriesTheUserAndTheSiteAdminRole_ButNoAgency()
    {
        var token = new JsonWebToken(CreateIssuer().IssueSiteAdmin(_userId).Value);

        Assert.Equal(_userId.ToString(), token.GetClaim(ClaimNames.UserId).Value);
        Assert.Equal("siteadmin", token.GetClaim(ClaimNames.Role).Value);
        Assert.False(token.TryGetClaim(ClaimNames.AgencyId, out _));
    }

    [Fact]
    public async Task IssueSiteAdmin_ProducesATokenTheApiAccepts_SignedWithHs256_ForFifteenMinutes()
    {
        var issued = CreateIssuer().IssueSiteAdmin(_userId);

        var result = await ValidateAsync(issued.Value);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("HS256", new JsonWebToken(issued.Value).Alg);
        Assert.Equal(_time.Now.AddMinutes(15), issued.ExpiresAt);
    }

    [Fact]
    public void IssueSiteAdmin_GivesEveryTokenItsOwnId()
    {
        var issuer = CreateIssuer();

        Assert.NotEqual(
            new JsonWebToken(issuer.IssueSiteAdmin(_userId).Value).Id,
            new JsonWebToken(issuer.IssueSiteAdmin(_userId).Value).Id);
    }

    [Fact]
    public void AnAgencyToken_NeverCarriesTheSiteAdminRole()
    {
        foreach (var role in Enum.GetValues<AgencyRole>())
        {
            var token = new JsonWebToken(CreateIssuer().Issue(_userId, _agencyId, role).Value);

            Assert.NotEqual("siteadmin", token.GetClaim(ClaimNames.Role).Value);
        }
    }

    [Fact]
    public async Task Issue_ATokenSignedByAnotherKey_IsNotValid()
    {
        var token = CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Owner).Value;
        _settings.SigningKey = new string('x', 48);

        Assert.False((await ValidateAsync(token)).IsValid);
    }

    [Fact]
    public async Task Issue_ATamperedToken_IsNotValid()
    {
        var parts = CreateIssuer().Issue(_userId, _agencyId, AgencyRole.Viewer).Value.Split('.');
        var forgedPayload = Convert.ToBase64String("""{"role":"owner"}"""u8.ToArray()).TrimEnd('=');

        Assert.False((await ValidateAsync($"{parts[0]}.{forgedPayload}.{parts[2]}")).IsValid);
    }
}
