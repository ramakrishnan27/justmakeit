using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class SiteAdminAuthServiceTests
{
    private const string Password = "a long enough password";

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly ISiteAdminRepository _siteAdmins = Substitute.For<ISiteAdminRepository>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly SiteAdminAuthService _service;

    private readonly Guid _userId = Guid.NewGuid();

    public SiteAdminAuthServiceTests()
    {
        _accessTokens.IssueSiteAdmin(Arg.Any<Guid>()).Returns(_ => new AccessToken("site-access-token", _time.Now.AddDays(30)));
        _siteAdmins.IsSiteAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        _service = CreateService(_hasher);
    }

    private SiteAdminAuthService CreateService(IPasswordHasher hasher) => new(
        _accounts, _siteAdmins, hasher, _accessTokens, _tenant, NullLogger<SiteAdminAuthService>.Instance);

    private UserLoginRecord StoredUser(string password = Password) => new()
    {
        Id = _userId,
        Email = "boss@example.com",
        FullName = "Site Admin",
        PasswordHash = _hasher.Hash(password)
    };

    private void GivenUser(UserLoginRecord? user) =>
        _accounts.FindLoginByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_AsASiteAdmin_IssuesASiteAdminToken_ForThatUser()
    {
        GivenUser(StoredUser());

        var result = await _service.LoginAsync(new SiteLoginRequest("boss@example.com", Password), CancellationToken.None);

        _accessTokens.Received(1).IssueSiteAdmin(_userId);
        Assert.Equal("site-access-token", result.AccessToken);
        Assert.Equal(_time.Now.AddDays(30), result.ExpiresAt);
    }

    [Fact]
    public async Task Login_LooksTheUserUpByTheNormalizedEmail()
    {
        GivenUser(null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("  Boss@Example.COM ", Password), CancellationToken.None));

        await _accounts.Received(1).FindLoginByEmailAsync("boss@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_IsRejected_AndIssuesNothing()
    {
        GivenUser(StoredUser());

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("boss@example.com", "the wrong password"), CancellationToken.None));

        Assert.Equal("invalid_credentials", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().IssueSiteAdmin(default);
    }

    [Fact]
    public async Task Login_ByAnOrdinaryUser_WithTheRightPassword_IsRejected_ExactlyLikeAWrongPassword()
    {
        _siteAdmins.IsSiteAdminAsync(_userId, Arg.Any<CancellationToken>()).Returns(false);
        GivenUser(StoredUser());
        var notAdmin = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("boss@example.com", Password), CancellationToken.None));

        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("boss@example.com", "the wrong password"), CancellationToken.None));

        // The two must be indistinguishable, or the endpoint would reveal which addresses are site admins.
        Assert.Equal(wrongPassword.Message, notAdmin.Message);
        Assert.Equal(wrongPassword.Code, notAdmin.Code);
        Assert.Equal(wrongPassword.StatusCode, notAdmin.StatusCode);
        _accessTokens.DidNotReceiveWithAnyArgs().IssueSiteAdmin(default);
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_FailsExactlyLikeAWrongPassword()
    {
        GivenUser(null);
        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("nobody@example.com", Password), CancellationToken.None));

        GivenUser(StoredUser());
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new SiteLoginRequest("boss@example.com", "the wrong password"), CancellationToken.None));

        Assert.Equal(wrong.Message, unknown.Message);
        Assert.Equal(wrong.Code, unknown.Code);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_StillSpendsTheTimeOfAHashCheck()
    {
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.DummyHash.Returns("pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        var service = CreateService(hasher);
        GivenUser(null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new SiteLoginRequest("nobody@example.com", Password), CancellationToken.None));

        hasher.Received(1).Verify(Password, hasher.DummyHash);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("not an email", "password")]
    [InlineData("boss@example.com", "")]
    public async Task Login_WithInvalidInput_IsABadRequest_WithoutLookingAnyoneUp(string email, string password)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.LoginAsync(new SiteLoginRequest(email, password), CancellationToken.None));

        await _accounts.DidNotReceiveWithAnyArgs().FindLoginByEmailAsync(default!, default);
    }

    // --- Me ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_ReturnsTheUserAndTheSiteAdminRole()
    {
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new UserRecord { Id = _userId, Email = "boss@example.com", FullName = "Site Admin" });

        var me = await _service.GetMeAsync(CancellationToken.None);

        Assert.Equal(new UserDto(_userId, "boss@example.com", "Site Admin"), me.User);
        Assert.Equal("siteadmin", me.Role);
    }

    [Fact]
    public async Task GetMe_AfterTheUserStoppedBeingASiteAdmin_IsUnauthorized()
    {
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);
        _siteAdmins.IsSiteAdminAsync(_userId, Arg.Any<CancellationToken>()).Returns(false);

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("clientmanager")]
    [InlineData(null)]
    public async Task GetMe_ForATokenThatIsNotASiteAdminToken_IsUnauthorized(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));

        await _accounts.DidNotReceiveWithAnyArgs().GetUserAsync(default, default);
    }

    [Fact]
    public async Task GetMe_ForADeletedUser_IsUnauthorized()
    {
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetMe_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }
}
