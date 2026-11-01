using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Helpers.Utilities;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class RegisterApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private async Task<HttpResponseMessage> RegisterRawAsync(
        string email, string organizationName = "Acme Agency", string? workspaceName = null, string businessModel = "agency") =>
        await Host.PostAsync("/v1/auth/register", new
        {
            email,
            password = ApiTestHost.DefaultPassword,
            fullName = "Ana Smith",
            organizationName,
            businessModel,
            workspaceName
        });

    [Fact]
    public async Task Register_Returns201_WithAFifteenMinuteAccessToken_AndNoRefreshTokenInTheBody()
    {
        var response = await RegisterRawAsync("ana@example.com");

        AssertStatus(HttpStatusCode.Created, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Register_AccessToken_IsHs256_FifteenMinutes_WithUserAgencyAndOrganizationAdminRole()
    {
        var session = await Host.RegisterAsync();

        var token = new JsonWebToken(session.AccessToken);

        Assert.Equal("HS256", token.Alg);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
        Assert.Equal(session.UserId.ToString(), token.GetClaim("user_id").Value);
        Assert.Equal(session.AgencyId.ToString(), token.GetClaim("agency_id").Value);
        Assert.Equal("organizationadmin", token.GetClaim("role").Value);
    }

    [Fact]
    public async Task Register_SetsTheRefreshCookie_HttpOnly_Secure_SameSiteStrict_ScopedToAuth()
    {
        var response = await RegisterRawAsync("ana@example.com");

        var cookie = ApiTestHost.RefreshCookieHeader(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_RefreshCookie_Is32RandomBytes_ExpiringInThirtyDays_AndNotInTheBody()
    {
        var response = await RegisterRawAsync("ana@example.com");

        var value = ApiTestHost.RefreshCookieValue(response)!;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", value);
        Assert.DoesNotContain(value, await response.Content.ReadAsStringAsync());

        var expires = DateTimeOffset.Parse(
            Regex.Match(ApiTestHost.RefreshCookieHeader(response)!, "expires=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value,
            CultureInfo.InvariantCulture);
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public async Task Register_StoresOnlyTheSha256HashOfTheRefreshToken()
    {
        var session = await Host.RegisterAsync();

        var hashes = await OwnerQueryAsync<string>(
            "SELECT token_hash FROM refresh_tokens WHERE user_id = @userId", new { userId = session.UserId });

        Assert.Equal(HashHelper.Sha256Hex(session.RefreshToken), hashes);
        Assert.NotEqual(session.RefreshToken, hashes);
    }

    [Fact]
    public async Task Register_StoresAPbkdf2Hash_Of600000Iterations_NeverThePassword()
    {
        var session = await Host.RegisterAsync();

        var stored = await OwnerQueryAsync<string>("SELECT password_hash FROM users WHERE id = @id", new { id = session.UserId });

        var parts = stored.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.DoesNotContain(ApiTestHost.DefaultPassword, stored);
    }

    [Fact]
    public async Task Register_CreatesATrialOrganization_AFounderMembership_AndAFirstWorkspace()
    {
        var session = await Host.RegisterAsync(agencyName: "Bright Ideas");

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", session.AccessToken));
        Assert.Equal("Bright Ideas", me.GetProperty("agency").GetProperty("name").GetString());
        Assert.Equal("trial", me.GetProperty("agency").GetProperty("plan").GetString());
        Assert.Equal("agency", me.GetProperty("agency").GetProperty("businessModel").GetString());
        Assert.Equal("organizationadmin", me.GetProperty("role").GetString());
        Assert.Single(me.GetProperty("memberships").EnumerateArray());

        var workspaces = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", session.AccessToken));
        Assert.Equal(["Main workspace"], workspaces.EnumerateArray().Select(w => w.GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData("company", "organizationadmin")]
    [InlineData("agency", "organizationadmin")]
    [InlineData("creator", "contentwriter")]
    public async Task Register_EachBusinessModel_GetsItsFounderRole_InTheTokenAndInMe_AndInTheDatabase(string model, string role)
    {
        var session = await Host.RegisterAsync(businessModel: model);

        Assert.Equal(role, new JsonWebToken(session.AccessToken).GetClaim("role").Value);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", session.AccessToken));
        Assert.Equal(model, me.GetProperty("agency").GetProperty("businessModel").GetString());
        Assert.Equal(role, me.GetProperty("role").GetString());
        var membership = Assert.Single(me.GetProperty("memberships").EnumerateArray());
        Assert.Equal(model, membership.GetProperty("businessModel").GetString());
        Assert.Equal(role, membership.GetProperty("role").GetString());

        Assert.Equal(model, await OwnerQueryAsync<string>(
            "SELECT business_model FROM agencies WHERE id = @id", new { id = session.AgencyId }));
        Assert.Equal((model, role), (
            await OwnerQueryAsync<string>("SELECT business_model FROM agency_members WHERE agency_id = @id", new { id = session.AgencyId }),
            await OwnerQueryAsync<string>("SELECT role FROM agency_members WHERE agency_id = @id", new { id = session.AgencyId })));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Company")]
    [InlineData("individual")]
    public async Task Register_WithoutAKnownBusinessModel_Returns400_AndCreatesNothing(string? businessModel)
    {
        var response = await RegisterRawAsync("ana@example.com", businessModel: businessModel!);

        AssertStatus(HttpStatusCode.BadRequest, response);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("businessModel", out _));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM agencies"));
    }

    [Fact]
    public async Task Register_WithTheOldAgencyNameField_IsNotAnOrganizationName()
    {
        // The field was renamed. A client that still sends agencyName is told what is missing, not guessed at.
        var response = await Host.PostAsync("/v1/auth/register", new
        {
            email = "ana@example.com", password = ApiTestHost.DefaultPassword, agencyName = "Acme", businessModel = "company"
        });

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("organizationName", out _));
    }

    [Fact]
    public async Task Register_WithAWorkspaceName_NamesTheFirstWorkspaceAfterIt()
    {
        var response = await RegisterRawAsync("ana@example.com", workspaceName: "  Client one ");
        var accessToken = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;

        var workspaces = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", accessToken));

        Assert.Equal(["Client one"], workspaces.EnumerateArray().Select(w => w.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Register_NormalizesTheEmail_SoLoginWorksInAnyCase()
    {
        await RegisterRawAsync("  Ana@Example.COM ");

        var stored = await OwnerQueryAsync<string>("SELECT email FROM users");
        Assert.Equal("ana@example.com", stored);
        var login = await Host.PostAsync("/v1/auth/login", new { email = "ANA@example.com", password = ApiTestHost.DefaultPassword });
        AssertStatus(HttpStatusCode.OK, login);
    }

    [Fact]
    public async Task Register_WithATakenEmail_Returns409_AndCreatesNothingNew()
    {
        await RegisterRawAsync("ana@example.com", "First agency");

        var response = await RegisterRawAsync("ANA@example.com", "Second agency");

        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("email_taken", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM agencies"));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM workspaces"));
    }

    [Fact]
    public async Task Register_WithInvalidInput_Returns400_ListingEachBadField_AndCreatesNothing()
    {
        var response = await Host.PostAsync("/v1/auth/register", new { email = "nope", password = "short", organizationName = "", businessModel = "nope" });

        AssertStatus(HttpStatusCode.BadRequest, response);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.Equal("Enter a valid email address.", errors.GetProperty("email")[0].GetString());
        Assert.Contains("at least 10", errors.GetProperty("password")[0].GetString());
        Assert.True(errors.TryGetProperty("organizationName", out _));
        Assert.Equal("Business model must be one of: company, agency, creator.", errors.GetProperty("businessModel")[0].GetString());
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM agencies"));
    }

    [Fact]
    public async Task Register_TwoPeopleAtOnce_WithTheSameEmail_CreatesOneAccount()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => Task.Run(() => RegisterRawAsync("race@example.com", $"Agency {i}"))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM agencies"));
    }

    [Fact]
    public async Task Register_GivesEveryUserTheirOwnAgency_AndNeitherSeesTheOther()
    {
        var ana = await Host.RegisterAsync(agencyName: "Ana Agency");
        var bob = await Host.RegisterAsync(agencyName: "Bob Agency");

        var anaMe = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", ana.AccessToken));
        var bobMe = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", bob.AccessToken));

        Assert.NotEqual(ana.AgencyId, bob.AgencyId);
        Assert.Equal("Ana Agency", anaMe.GetProperty("agency").GetProperty("name").GetString());
        Assert.Equal("Bob Agency", bobMe.GetProperty("agency").GetProperty("name").GetString());
        Assert.Single(anaMe.GetProperty("memberships").EnumerateArray());
        Assert.Single(bobMe.GetProperty("memberships").EnumerateArray());
    }
}
