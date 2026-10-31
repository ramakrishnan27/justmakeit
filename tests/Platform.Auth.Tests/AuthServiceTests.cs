using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Tests;

public sealed class AuthServiceTests
{
    private const string Password = "a long enough password";

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly IAgencyRepository _agencies = Substitute.For<IAgencyRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly JwtSettings _jwt = new() { SigningKey = new string('k', 32) };
    private readonly AuthService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();

    public AuthServiceTests()
    {
        _accessTokens.Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<AgencyRole>())
            .Returns(call => new AccessToken("access-token", _time.Now.AddMinutes(15)));
        _accounts.CreateAccountAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(true);
        _refreshTokens.RotateAsync(Arg.Any<Guid>(), Arg.Any<NewRefreshToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _service = new AuthService(
            _accounts, _memberships, _agencies, _refreshTokens, _hasher, _accessTokens,
            _tenant, Options.Create(_jwt), _time, NullLogger<AuthService>.Instance);
    }

    private static RegisterRequest ValidRegistration() =>
        new("  Ana@Example.COM ", Password, " Ana Smith ", " Acme Agency ", null);

    private UserLoginRecord StoredUser(string password = Password) => new()
    {
        Id = _userId,
        Email = "ana@example.com",
        FullName = "Ana Smith",
        PasswordHash = _hasher.Hash(password)
    };

    private static MembershipRecord Membership(Guid agencyId, AgencyRole role, string name = "Agency") =>
        new() { AgencyId = agencyId, AgencyName = name, Role = role };

    private void GivenUser(UserLoginRecord? user) =>
        _accounts.FindLoginByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

    private void GivenMemberships(params MembershipRecord[] memberships) =>
        _memberships.ListForUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(memberships);

    private RefreshTokenRecord GivenStoredRefreshToken(
        string rawToken,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null,
        Guid? userId = null,
        Guid? agencyId = null)
    {
        var record = new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _userId,
            AgencyId = agencyId ?? _agencyId,
            ExpiresAt = expiresAt ?? _time.Now.AddDays(10),
            RevokedAt = revokedAt
        };
        _refreshTokens.FindByHashAsync(HashHelper.Sha256Hex(rawToken), Arg.Any<CancellationToken>()).Returns(record);
        return record;
    }

    private void GivenMembership(AgencyRole role) =>
        _memberships.FindAsync(_userId, _agencyId, Arg.Any<CancellationToken>()).Returns(Membership(_agencyId, role));

    private static async Task<T> AssertThrowsAsync<T>(Func<Task> action) where T : AppException =>
        await Assert.ThrowsAsync<T>(action);

    // --- Register ----------------------------------------------------------------------------

    [Fact]
    public async Task Register_CreatesTheAccount_WithANormalizedEmailTrimmedNamesAndAHashedPassword()
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal("ana@example.com", created.Email);
        Assert.Equal("Ana Smith", created.FullName);
        Assert.Equal("Acme Agency", created.AgencyName);
        Assert.NotEqual(Password, created.PasswordHash);
        Assert.DoesNotContain(Password, created.PasswordHash);
        Assert.True(_hasher.Verify(Password, created.PasswordHash));
        Assert.NotEqual(Guid.Empty, created.UserId);
        Assert.NotEqual(Guid.Empty, created.AgencyId);
        Assert.NotEqual(created.UserId, created.AgencyId);
    }

    [Fact]
    public async Task Register_WithoutAWorkspaceName_CreatesTheDefaultWorkspace()
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.Equal(AuthService.DefaultWorkspaceName, created!.WorkspaceName);
    }

    [Fact]
    public async Task Register_WithAWorkspaceName_UsesItTrimmed()
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        await _service.RegisterAsync(ValidRegistration() with { WorkspaceName = "  Client one " }, CancellationToken.None);

        Assert.Equal("Client one", created!.WorkspaceName);
    }

    [Fact]
    public async Task Register_SignsTheNewOwnerIn_WithAnOwnerRoleTokenForTheNewAgency()
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        _accessTokens.Received(1).Issue(created!.UserId, created.AgencyId, AgencyRole.Owner);
        Assert.Equal("access-token", result.Tokens.AccessToken);
        Assert.Equal("Bearer", result.Tokens.TokenType);
        Assert.Equal(15 * 60, result.Tokens.ExpiresIn);
    }

    [Fact]
    public async Task Register_StoresOnlyTheHashOfTheRefreshToken_ForTheNewUserAndAgency()
    {
        NewAccount? created = null;
        NewRefreshToken? stored = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);
        await _refreshTokens.InsertAsync(Arg.Do<NewRefreshToken>(token => stored = token), Arg.Any<CancellationToken>());

        var result = await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(HashHelper.Sha256Hex(result.RefreshToken), stored.TokenHash);
        Assert.NotEqual(result.RefreshToken, stored.TokenHash);
        Assert.Equal(created!.UserId, stored.UserId);
        Assert.Equal(created.AgencyId, stored.AgencyId);
    }

    [Fact]
    public async Task Register_IssuesA32ByteRefreshToken_ThatExpiresAfterTheConfiguredDays()
    {
        var result = await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.Equal(43, result.RefreshToken.Length); // 32 random bytes as unpadded base64url
        Assert.Equal(_time.Now.AddDays(30), result.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Register_WithATakenEmail_ThrowsEmailTaken_AndSignsNobodyIn()
    {
        _accounts.CreateAccountAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(false);

        var exception = await AssertThrowsAsync<ConflictException>(
            () => _service.RegisterAsync(ValidRegistration(), CancellationToken.None));

        Assert.Equal("email_taken", exception.Code);
        await _refreshTokens.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default);
    }

    [Fact]
    public async Task Register_WithInvalidInput_ReportsEveryBadField_AndTouchesNothing()
    {
        var request = new RegisterRequest("not an email", "short", null, " ", null);

        var exception = await AssertThrowsAsync<BadRequestException>(() => _service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("email", exception.Errors!.Keys);
        Assert.Contains("password", exception.Errors.Keys);
        Assert.Contains("agencyName", exception.Errors.Keys);
        await _accounts.DidNotReceiveWithAnyArgs().CreateAccountAsync(default!, default);
    }

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_WithTheRightPassword_UsesTheMostRecentMembership()
    {
        var recent = Guid.NewGuid();
        GivenUser(StoredUser());
        GivenMemberships(Membership(recent, AgencyRole.Manager), Membership(_agencyId, AgencyRole.Owner));

        var result = await _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, recent, AgencyRole.Manager);
        Assert.Equal("access-token", result.Tokens.AccessToken);
        await _memberships.Received(1).TouchAsync(recent, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithARequestedAgency_UsesThatMembershipAndRole()
    {
        var other = Guid.NewGuid();
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, AgencyRole.Owner), Membership(other, AgencyRole.Viewer));

        await _service.LoginAsync(new LoginRequest("ana@example.com", Password, other), CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, other, AgencyRole.Viewer);
    }

    [Fact]
    public async Task Login_WithAnAgencyTheUserDoesNotBelongTo_IsForbidden_AndIssuesNothing()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, AgencyRole.Owner));

        var exception = await AssertThrowsAsync<ForbiddenException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", Password, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("not_a_member", exception.Code);
        await _refreshTokens.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Login_StoresOnlyTheHashOfTheRefreshToken()
    {
        NewRefreshToken? stored = null;
        await _refreshTokens.InsertAsync(Arg.Do<NewRefreshToken>(token => stored = token), Arg.Any<CancellationToken>());
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, AgencyRole.Owner));

        var result = await _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None);

        Assert.Equal(HashHelper.Sha256Hex(result.RefreshToken), stored!.TokenHash);
        Assert.Equal(_time.Now.AddDays(30), stored.ExpiresAt);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_IsRejected_BeforeAnythingElseIsLoaded()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, AgencyRole.Owner));

        var exception = await AssertThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", "the wrong password", null), CancellationToken.None));

        Assert.Equal("invalid_credentials", exception.Code);
        await _memberships.DidNotReceiveWithAnyArgs().ListForUserAsync(default, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_FailsExactlyLikeAWrongPassword()
    {
        GivenUser(null);
        var unknown = await AssertThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequest("nobody@example.com", Password, null), CancellationToken.None));

        GivenUser(StoredUser());
        var wrong = await AssertThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", "the wrong password", null), CancellationToken.None));

        Assert.Equal(wrong.Message, unknown.Message);
        Assert.Equal(wrong.Code, unknown.Code);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_StillSpendsTheTimeOfAHashCheck()
    {
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.DummyHash.Returns("pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        var service = new AuthService(
            _accounts, _memberships, _agencies, _refreshTokens, hasher, _accessTokens,
            _tenant, Options.Create(_jwt), _time, NullLogger<AuthService>.Instance);
        GivenUser(null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest("nobody@example.com", Password, null), CancellationToken.None));

        hasher.Received(1).Verify(Password, hasher.DummyHash);
    }

    [Fact]
    public async Task Login_LooksTheUserUpByTheNormalizedEmail()
    {
        GivenUser(null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequest("  Ana@Example.COM ", Password, null), CancellationToken.None));

        await _accounts.Received(1).FindLoginByEmailAsync("ana@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_ForAUserWithNoMemberships_IsForbidden()
    {
        GivenUser(StoredUser());
        GivenMemberships();

        var exception = await AssertThrowsAsync<ForbiddenException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None));

        Assert.Equal("no_agency", exception.Code);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("not an email", "password")]
    [InlineData("ana@example.com", "")]
    public async Task Login_WithInvalidInput_IsABadRequest_WithoutLookingAnyoneUp(string email, string password)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.LoginAsync(new LoginRequest(email, password, null), CancellationToken.None));

        await _accounts.DidNotReceiveWithAnyArgs().FindLoginByEmailAsync(default!, default);
    }

    // --- Refresh -----------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_RotatesTheToken_RevokingTheOldAndStoringANewOne()
    {
        const string presented = "the-presented-token";
        var record = GivenStoredRefreshToken(presented);
        GivenMembership(AgencyRole.Admin);
        NewRefreshToken? replacement = null;
        _refreshTokens.RotateAsync(
                record.Id, Arg.Do<NewRefreshToken>(token => replacement = token), _time.Now, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _service.RefreshAsync(presented, CancellationToken.None);

        Assert.NotNull(replacement);
        Assert.NotEqual(presented, result.RefreshToken);
        Assert.Equal(HashHelper.Sha256Hex(result.RefreshToken), replacement.TokenHash);
        Assert.Equal(_userId, replacement.UserId);
        Assert.Equal(_agencyId, replacement.AgencyId);
        Assert.Equal(_time.Now.AddDays(30), result.RefreshTokenExpiresAt);
        _accessTokens.Received(1).Issue(_userId, _agencyId, AgencyRole.Admin);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, default, default);
    }

    [Fact]
    public async Task Refresh_LooksTheTokenUpByItsHash_NeverByItsValue()
    {
        const string presented = "the-presented-token";
        GivenStoredRefreshToken(presented);
        GivenMembership(AgencyRole.Owner);

        await _service.RefreshAsync(presented, CancellationToken.None);

        await _refreshTokens.Received(1).FindByHashAsync(HashHelper.Sha256Hex(presented), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceive().FindByHashAsync(presented, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_TakesTheRoleFromTheDatabase_SoAChangedRoleAppliesAtOnce()
    {
        GivenStoredRefreshToken("token");
        GivenMembership(AgencyRole.Viewer);

        await _service.RefreshAsync("token", CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, _agencyId, AgencyRole.Viewer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refresh_WithoutAToken_IsUnauthorized_WithoutHittingTheDatabase(string? token)
    {
        var exception = await AssertThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(token, CancellationToken.None));

        Assert.Equal("invalid_refresh_token", exception.Code);
        await _refreshTokens.DidNotReceiveWithAnyArgs().FindByHashAsync(default!, default);
    }

    [Fact]
    public async Task Refresh_WithAnOversizedToken_IsUnauthorized_WithoutHittingTheDatabase()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync(new string('a', 5_000), CancellationToken.None));

        await _refreshTokens.DidNotReceiveWithAnyArgs().FindByHashAsync(default!, default);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownToken_IsUnauthorized_AndRotatesNothing()
    {
        _refreshTokens.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshTokenRecord?)null);

        var exception = await AssertThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("unknown", CancellationToken.None));

        Assert.Equal("invalid_refresh_token", exception.Code);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RotateAsync(default, default!, default, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, default, default);
    }

    [Fact]
    public async Task Refresh_WithAnExpiredToken_IsUnauthorized_AndRotatesNothing()
    {
        GivenStoredRefreshToken("expired", expiresAt: _time.Now.AddSeconds(-1));
        GivenMembership(AgencyRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("expired", CancellationToken.None));

        await _refreshTokens.DidNotReceiveWithAnyArgs().RotateAsync(default, default!, default, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, default, default);
    }

    [Fact]
    public async Task Refresh_WithATokenExpiringExactlyNow_IsUnauthorized()
    {
        GivenStoredRefreshToken("edge", expiresAt: _time.Now);
        GivenMembership(AgencyRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("edge", CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_WithARevokedToken_IsReuse_AndRevokesEveryTokenOfThatUserForThatAgency()
    {
        GivenStoredRefreshToken("stolen", revokedAt: _time.Now.AddMinutes(-5));
        GivenMembership(AgencyRole.Owner);

        var exception = await AssertThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("stolen", CancellationToken.None));

        Assert.Equal("invalid_refresh_token", exception.Code);
        await _refreshTokens.Received(1).RevokeAllAsync(_agencyId, _userId, _time.Now, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().RotateAsync(default, default!, default, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default);
    }

    [Fact]
    public async Task Refresh_ReuseOfARevokedExpiredToken_IsStillDetected()
    {
        GivenStoredRefreshToken("old", expiresAt: _time.Now.AddDays(-1), revokedAt: _time.Now.AddDays(-2));

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("old", CancellationToken.None));

        await _refreshTokens.Received(1).RevokeAllAsync(_agencyId, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WhenTwoRequestsRaceForTheSameToken_TheLoserIsTreatedAsReuse()
    {
        var record = GivenStoredRefreshToken("raced");
        GivenMembership(AgencyRole.Owner);
        _refreshTokens.RotateAsync(record.Id, Arg.Any<NewRefreshToken>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("raced", CancellationToken.None));

        await _refreshTokens.Received(1).RevokeAllAsync(_agencyId, _userId, _time.Now, Arg.Any<CancellationToken>());
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default);
    }

    [Fact]
    public async Task Refresh_AfterTheMembershipEnded_IsUnauthorized_AndEndsTheUsersSessionsThere()
    {
        GivenStoredRefreshToken("ex-member");
        _memberships.FindAsync(_userId, _agencyId, Arg.Any<CancellationToken>()).Returns((MembershipRecord?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("ex-member", CancellationToken.None));

        await _refreshTokens.Received(1).RevokeAllAsync(_agencyId, _userId, _time.Now, Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().RotateAsync(default, default!, default, default);
    }

    // --- Logout ------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_RevokesTheTokenItWasGiven()
    {
        var record = GivenStoredRefreshToken("leaving");

        await _service.LogoutAsync("leaving", CancellationToken.None);

        await _refreshTokens.Received(1).RevokeAsync(_agencyId, _userId, record.Id, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_WithAnUnknownToken_DoesNothingAndDoesNotFail()
    {
        _refreshTokens.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshTokenRecord?)null);

        await _service.LogoutAsync("unknown", CancellationToken.None);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Logout_WithoutAToken_DoesNothingAndDoesNotFail(string? token)
    {
        await _service.LogoutAsync(token, CancellationToken.None);

        await _refreshTokens.DidNotReceiveWithAnyArgs().FindByHashAsync(default!, default);
    }

    [Fact]
    public async Task Logout_WithAnAlreadyRevokedToken_DoesNotTriggerReuseDetection()
    {
        GivenStoredRefreshToken("again", revokedAt: _time.Now.AddMinutes(-1));

        await _service.LogoutAsync("again", CancellationToken.None);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default, default, default);
    }

    // --- Switch agency -----------------------------------------------------------------------

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserBelongsTo_IssuesTokensWithThatRole()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "owner");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>()).Returns(Membership(target, AgencyRole.Viewer));

        var result = await _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), null, CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, target, AgencyRole.Viewer);
        Assert.Equal("access-token", result.Tokens.AccessToken);
        await _memberships.Received(1).TouchAsync(target, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchAgency_StoresANewRefreshTokenForTheTargetAgency_AndRevokesTheOldOne()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "owner");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>()).Returns(Membership(target, AgencyRole.Manager));
        var current = GivenStoredRefreshToken("current-cookie");
        NewRefreshToken? stored = null;
        await _refreshTokens.InsertAsync(Arg.Do<NewRefreshToken>(token => stored = token), Arg.Any<CancellationToken>());

        var result = await _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), "current-cookie", CancellationToken.None);

        Assert.Equal(target, stored!.AgencyId);
        Assert.Equal(_userId, stored.UserId);
        Assert.Equal(HashHelper.Sha256Hex(result.RefreshToken), stored.TokenHash);
        await _refreshTokens.Received(1).RevokeAsync(_agencyId, _userId, current.Id, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchAgency_NeverRevokesAnotherUsersRefreshToken()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "owner");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>()).Returns(Membership(target, AgencyRole.Manager));
        GivenStoredRefreshToken("someone-elses", userId: Guid.NewGuid());

        await _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), "someone-elses", CancellationToken.None);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserDoesNotBelongTo_IsForbidden_AndIssuesNothing()
    {
        _tenant.Set(_agencyId, _userId, "owner");
        _memberships.FindAsync(_userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MembershipRecord?)null);

        var exception = await AssertThrowsAsync<ForbiddenException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(Guid.NewGuid()), "cookie", CancellationToken.None));

        Assert.Equal("not_a_member", exception.Code);
        await _refreshTokens.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task SwitchAgency_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(Guid.NewGuid()), null, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchAgency_WithoutAnAgencyId_IsABadRequest(bool emptyGuid)
    {
        _tenant.Set(_agencyId, _userId, "owner");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(emptyGuid ? Guid.Empty : null), null, CancellationToken.None));
    }

    // --- Me ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_ReturnsTheUserCurrentAgencyRoleAndEveryMembership()
    {
        var other = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "owner");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new UserRecord { Id = _userId, Email = "ana@example.com", FullName = "Ana Smith" });
        GivenMemberships(Membership(_agencyId, AgencyRole.Admin, "Acme"), Membership(other, AgencyRole.Viewer, "Other"));
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>())
            .Returns(new AgencyRecord { Id = _agencyId, Name = "Acme", Plan = AgencyPlan.Trial });

        var me = await _service.GetMeAsync(CancellationToken.None);

        Assert.Equal(new UserDto(_userId, "ana@example.com", "Ana Smith"), me.User);
        Assert.Equal(new AgencyDto(_agencyId, "Acme", AgencyPlan.Trial), me.Agency);
        Assert.Equal(AgencyRole.Admin, me.Role);
        Assert.Equal(
            [new MembershipDto(_agencyId, "Acme", AgencyRole.Admin), new MembershipDto(other, "Other", AgencyRole.Viewer)],
            me.Memberships);
    }

    [Fact]
    public async Task GetMe_TakesTheRoleFromTheDatabase_NotFromTheToken()
    {
        _tenant.Set(_agencyId, _userId, "owner");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(new UserRecord { Id = _userId });
        GivenMemberships(Membership(_agencyId, AgencyRole.Viewer));
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(new AgencyRecord { Id = _agencyId });

        Assert.Equal(AgencyRole.Viewer, (await _service.GetMeAsync(CancellationToken.None)).Role);
    }

    [Fact]
    public async Task GetMe_AfterTheMembershipEnded_IsUnauthorized()
    {
        _tenant.Set(_agencyId, _userId, "owner");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(new UserRecord { Id = _userId });
        GivenMemberships(Membership(Guid.NewGuid(), AgencyRole.Owner));

        var exception = await AssertThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));

        Assert.Equal("membership_ended", exception.Code);
    }

    [Fact]
    public async Task GetMe_ForADeletedUser_IsUnauthorized()
    {
        _tenant.Set(_agencyId, _userId, "owner");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetMe_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }
}
