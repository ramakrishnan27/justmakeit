using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Tests;

public sealed class AuthServiceTests
{
    private const string Password = "a long enough password";

    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly IAgencyRepository _agencies = Substitute.For<IAgencyRepository>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly AuthService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();

    public AuthServiceTests()
    {
        _accessTokens.Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrganizationRole>(), Arg.Any<PermissionGrants>())
            .Returns(call => new AccessToken("access-token", _time.Now.AddDays(30)));
        _permissions.GetGrantsAsync(Arg.Any<BusinessModel>(), Arg.Any<OrganizationRole>(), Arg.Any<CancellationToken>())
            .Returns(call => new PermissionGrants(7, [$"granted-to-{call.ArgAt<OrganizationRole>(1).ToDb()}"]));
        _accounts.CreateAccountAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(true);

        _service = new AuthService(_accounts, _memberships, _agencies, _invitations, _hasher, _accessTokens, _permissions, _tenant, _time, NullLogger<AuthService>.Instance);
    }

    private static RegisterRequest ValidRegistration(string businessModel = "agency") =>
        new("  Ana@Example.COM ", Password, " Ana Smith ", " Acme Agency ", businessModel, null);

    private UserLoginRecord StoredUser(string password = Password) => new()
    {
        Id = _userId,
        Email = "ana@example.com",
        FullName = "Ana Smith",
        PasswordHash = _hasher.Hash(password)
    };

    private static MembershipRecord Membership(
        Guid agencyId, OrganizationRole role, string name = "Agency", BusinessModel businessModel = BusinessModel.Agency) =>
        new() { AgencyId = agencyId, AgencyName = name, BusinessModel = businessModel, Role = role };

    private void GivenUser(UserLoginRecord? user) =>
        _accounts.FindLoginByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(user);

    private void GivenMemberships(params MembershipRecord[] memberships) =>
        _memberships.ListForUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(memberships);

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
        Assert.True(_hasher.Verify(Password, created.PasswordHash!));
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

    [Theory]
    [InlineData("company", BusinessModel.Company, OrganizationRole.OrganizationAdmin)]
    [InlineData("agency", BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]
    [InlineData("creator", BusinessModel.Creator, OrganizationRole.ContentWriter)]
    public async Task Register_SignsTheFounderIn_WithTheFounderRoleOfTheChosenModel_ForTheNewOrganization(
        string text, BusinessModel model, OrganizationRole founderRole)
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RegisterAsync(ValidRegistration(text), CancellationToken.None);

        Assert.Equal(model, created!.BusinessModel);
        Assert.Equal(founderRole, created.Role);
        Assert.True(created.BusinessModel.IsAllowed(created.Role));
        _accessTokens.Received(1).Issue(created.UserId, created.AgencyId, founderRole, Arg.Any<PermissionGrants>());
        Assert.Equal("access-token", result.AccessToken);
    }

    [Fact]
    public async Task Register_ReturnsTheTokenAndWhenItEnds_BothFromTheIssuer()
    {
        var result = await _service.RegisterAsync(ValidRegistration(), CancellationToken.None);

        // The expiry is the token's own, so the cookie and the token end together.
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(_time.Now.AddDays(30), result.ExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Company")]
    [InlineData("individual")]
    [InlineData("1")]
    public async Task Register_WithoutAKnownBusinessModel_IsABadRequest_AndCreatesNothing(string? businessModel)
    {
        var request = ValidRegistration() with { BusinessModel = businessModel };

        var exception = await AssertThrowsAsync<BadRequestException>(() => _service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(["businessModel"], exception.Errors!.Keys);
        await _accounts.DidNotReceiveWithAnyArgs().CreateAccountAsync(default!, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Register_WithATakenEmail_ThrowsEmailTaken_AndSignsNobodyIn()
    {
        _accounts.CreateAccountAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(false);

        var exception = await AssertThrowsAsync<ConflictException>(
            () => _service.RegisterAsync(ValidRegistration(), CancellationToken.None));

        Assert.Equal("email_taken", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Register_WithInvalidInput_ReportsEveryBadField_AndTouchesNothing()
    {
        var request = new RegisterRequest("not an email", "short", null, " ", "nope", null);

        var exception = await AssertThrowsAsync<BadRequestException>(() => _service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("email", exception.Errors!.Keys);
        Assert.Contains("password", exception.Errors.Keys);
        Assert.Contains("organizationName", exception.Errors.Keys);
        Assert.Contains("businessModel", exception.Errors.Keys);
        await _accounts.DidNotReceiveWithAnyArgs().CreateAccountAsync(default!, default);
    }

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_WithTheRightPassword_UsesTheMostRecentMembership()
    {
        var recent = Guid.NewGuid();
        GivenUser(StoredUser());
        GivenMemberships(Membership(recent, OrganizationRole.MarketingManager), Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        var result = await _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, recent, OrganizationRole.MarketingManager, Arg.Any<PermissionGrants>());
        Assert.Equal("access-token", result.AccessToken);
        await _memberships.Received(1).TouchAsync(recent, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithARequestedAgency_UsesThatMembershipAndRole()
    {
        var other = Guid.NewGuid();
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.OrganizationAdmin), Membership(other, OrganizationRole.ClientManager));

        await _service.LoginAsync(new LoginRequest("ana@example.com", Password, other), CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, other, OrganizationRole.ClientManager, Arg.Any<PermissionGrants>());
    }

    [Fact]
    public async Task Login_WithAnAgencyTheUserDoesNotBelongTo_IsForbidden_AndIssuesNothing()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        var exception = await AssertThrowsAsync<ForbiddenException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", Password, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("not_a_member", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Login_ReturnsTheTokenAndWhenItEnds_BothFromTheIssuer()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        var result = await _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(_time.Now.AddDays(30), result.ExpiresAt);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_IsRejected_BeforeAnythingElseIsLoaded()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        var exception = await AssertThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequest("ana@example.com", "the wrong password", null), CancellationToken.None));

        Assert.Equal("invalid_credentials", exception.Code);
        await _memberships.DidNotReceiveWithAnyArgs().ListForUserAsync(default, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
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
        var service = new AuthService(_accounts, _memberships, _agencies, _invitations, hasher, _accessTokens, _permissions, _tenant, _time, NullLogger<AuthService>.Instance);
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

    // --- Permissions in the token ------------------------------------------------------------

    [Theory]
    [InlineData(BusinessModel.Company, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Creator, OrganizationRole.ContentWriter)]
    public async Task Register_PutsThePermissionsOfTheFoundersSlotInTheToken(BusinessModel model, OrganizationRole founder)
    {
        await _service.RegisterAsync(ValidRegistration(model.ToDb()), CancellationToken.None);

        await _permissions.Received(1).GetGrantsAsync(model, founder, Arg.Any<CancellationToken>());
        _accessTokens.Received(1).Issue(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            founder,
            Arg.Is<PermissionGrants>(g => g.Version == 7 && g.Keys.SequenceEqual(new[] { $"granted-to-{founder.ToDb()}" })));
    }

    [Fact]
    public async Task Login_PutsThePermissionsOfTheMembershipsModelAndRoleInTheToken()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.MarketingManager, businessModel: BusinessModel.Company));

        await _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None);

        await _permissions.Received(1).GetGrantsAsync(BusinessModel.Company, OrganizationRole.MarketingManager, Arg.Any<CancellationToken>());
        _accessTokens.Received(1).Issue(
            _userId, _agencyId, OrganizationRole.MarketingManager, Arg.Is<PermissionGrants>(g => g.Keys.Contains("granted-to-marketingmanager")));
    }

    [Fact]
    public async Task SwitchAgency_PutsThePermissionsOfTheTargetsModelAndRoleInTheToken()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>())
            .Returns(Membership(target, OrganizationRole.ContentWriter, businessModel: BusinessModel.Creator));

        await _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), CancellationToken.None);

        await _permissions.Received(1).GetGrantsAsync(BusinessModel.Creator, OrganizationRole.ContentWriter, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Register_WhenThePermissionsCannotBeRead_CreatesNoAccount()
    {
        GivenThePermissionsCannotBeRead();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegisterAsync(ValidRegistration(), CancellationToken.None));

        await _accounts.DidNotReceiveWithAnyArgs().CreateAccountAsync(default!, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Login_WhenThePermissionsCannotBeRead_IssuesNothingAndTouchesNothing()
    {
        GivenUser(StoredUser());
        GivenMemberships(Membership(_agencyId, OrganizationRole.ContentWriter));
        GivenThePermissionsCannotBeRead();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.LoginAsync(new LoginRequest("ana@example.com", Password, null), CancellationToken.None));

        await _memberships.DidNotReceiveWithAnyArgs().TouchAsync(default, default, default, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task SwitchAgency_WhenThePermissionsCannotBeRead_ChangesNothing()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>()).Returns(Membership(target, OrganizationRole.ClientManager));
        GivenThePermissionsCannotBeRead();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), CancellationToken.None));

        await _memberships.DidNotReceiveWithAnyArgs().TouchAsync(default, default, default, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    private void GivenThePermissionsCannotBeRead() =>
        _permissions.GetGrantsAsync(Arg.Any<BusinessModel>(), Arg.Any<OrganizationRole>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The database is not reachable."));

    // --- Switch agency -----------------------------------------------------------------------

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserBelongsTo_IssuesATokenWithThatRole()
    {
        var target = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _memberships.FindAsync(_userId, target, Arg.Any<CancellationToken>()).Returns(Membership(target, OrganizationRole.ClientManager));

        var result = await _service.SwitchAgencyAsync(new SwitchAgencyRequest(target), CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, target, OrganizationRole.ClientManager, Arg.Any<PermissionGrants>());
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(_time.Now.AddDays(30), result.ExpiresAt);
        await _memberships.Received(1).TouchAsync(target, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserDoesNotBelongTo_IsForbidden_AndIssuesNothing()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _memberships.FindAsync(_userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MembershipRecord?)null);

        var exception = await AssertThrowsAsync<ForbiddenException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("not_a_member", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task SwitchAgency_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchAgency_WithoutAnAgencyId_IsABadRequest(bool emptyGuid)
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SwitchAgencyAsync(new SwitchAgencyRequest(emptyGuid ? Guid.Empty : null), CancellationToken.None));
    }

    // --- Me ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_ReturnsTheUserCurrentAgencyRoleAndEveryMembership()
    {
        var other = Guid.NewGuid();
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new UserRecord { Id = _userId, Email = "ana@example.com", FullName = "Ana Smith" });
        GivenMemberships(
            Membership(_agencyId, OrganizationRole.ContentWriter, "Acme", BusinessModel.Company),
            Membership(other, OrganizationRole.ClientManager, "Other", BusinessModel.Agency));
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>())
            .Returns(new AgencyRecord { Id = _agencyId, Name = "Acme", BusinessModel = BusinessModel.Company, Plan = AgencyPlan.Trial });

        var me = await _service.GetMeAsync(CancellationToken.None);

        Assert.Equal(new UserDto(_userId, "ana@example.com", "Ana Smith"), me.User);
        Assert.Equal(new AgencyDto(_agencyId, "Acme", BusinessModel.Company, AgencyPlan.Trial), me.Agency);
        Assert.Equal(OrganizationRole.ContentWriter, me.Role);
        Assert.Equal(
            [
                new MembershipDto(_agencyId, "Acme", BusinessModel.Company, OrganizationRole.ContentWriter),
                new MembershipDto(other, "Other", BusinessModel.Agency, OrganizationRole.ClientManager)
            ],
            me.Memberships);
    }

    [Fact]
    public async Task GetMe_TakesTheRoleFromTheDatabase_NotFromTheToken()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(new UserRecord { Id = _userId });
        GivenMemberships(Membership(_agencyId, OrganizationRole.ClientManager));
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(new AgencyRecord { Id = _agencyId });

        Assert.Equal(OrganizationRole.ClientManager, (await _service.GetMeAsync(CancellationToken.None)).Role);
    }

    [Fact]
    public async Task GetMe_AfterTheMembershipEnded_IsUnauthorized()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(new UserRecord { Id = _userId });
        GivenMemberships(Membership(Guid.NewGuid(), OrganizationRole.OrganizationAdmin));

        var exception = await AssertThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));

        Assert.Equal("membership_ended", exception.Code);
    }

    [Fact]
    public async Task GetMe_ForADeletedUser_IsUnauthorized()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");
        _accounts.GetUserAsync(_userId, Arg.Any<CancellationToken>()).Returns((UserRecord?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetMe_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMeAsync(CancellationToken.None));
    }

    // --- A person who signed up with Google has no password -----------------------------------

    [Theory]
    [InlineData("a long enough password")]
    [InlineData("")]
    [InlineData("pbkdf2")]
    [InlineData("anything at all")]
    public async Task Login_ForAPersonWithNoPassword_IsTheSame401AsAWrongPassword_WhateverIsTyped(string typed)
    {
        GivenUser(new UserLoginRecord { Id = _userId, Email = "maya@gmail.com", FullName = "Maya", PasswordHash = null });
        GivenMemberships(Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        var exception = await AssertThrowsAsync<UnauthorizedException>(
            () => _service.LoginAsync(new LoginRequest("maya@gmail.com", typed.Length == 0 ? "x" : typed, null), CancellationToken.None));

        Assert.Equal("invalid_credentials", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    // --- RegisterExternal --------------------------------------------------------------------

    private static ExternalRegistration GoogleRegistration(BusinessModel model = BusinessModel.Agency, string? organization = " Bright Ideas ") =>
        new("google", "sub-maya", " Maya.Rao@Gmail.com ", " Maya Rao ", organization, model);

    [Theory]
    [InlineData(BusinessModel.Company, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Creator, OrganizationRole.ContentWriter)]
    public async Task RegisterExternal_CreatesTheOrganization_WithNoPassword_AndTheGoogleIdentity(BusinessModel model, OrganizationRole founderRole)
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        await _service.RegisterExternalAsync(GoogleRegistration(model), CancellationToken.None);

        Assert.NotNull(created);
        Assert.Null(created.PasswordHash);
        Assert.Equal("maya.rao@gmail.com", created.Email);
        Assert.Equal("Maya Rao", created.FullName);
        Assert.Equal("Bright Ideas", created.AgencyName);
        Assert.Equal(model, created.BusinessModel);
        Assert.Equal(founderRole, created.Role);
        Assert.Equal(AuthService.DefaultWorkspaceName, created.WorkspaceName);
        Assert.Equal(new ExternalIdentity("google", "sub-maya", "maya.rao@gmail.com"), created.Identity);
    }

    [Fact]
    public async Task RegisterExternal_SignsTheFounderIn_WithTheFounderRoleToken()
    {
        NewAccount? created = null;
        _accounts.CreateAccountAsync(Arg.Do<NewAccount>(account => created = account), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.RegisterExternalAsync(GoogleRegistration(BusinessModel.Creator), CancellationToken.None);

        _accessTokens.Received(1).Issue(created!.UserId, created.AgencyId, OrganizationRole.ContentWriter, Arg.Any<PermissionGrants>());
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(_time.Now.AddDays(30), result.ExpiresAt);
    }

    [Fact]
    public async Task RegisterExternal_WithATakenEmail_IsAConflict_AndSignsNobodyIn()
    {
        _accounts.CreateAccountAsync(Arg.Any<NewAccount>(), Arg.Any<CancellationToken>()).Returns(false);

        var exception = await AssertThrowsAsync<ConflictException>(() => _service.RegisterExternalAsync(GoogleRegistration(), CancellationToken.None));

        Assert.Equal("email_taken", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Theory]
    [InlineData("not an email", "Acme", "sub", "email")]
    [InlineData("maya@gmail.com", "", "sub", "organizationName")]
    [InlineData("maya@gmail.com", "   ", "sub", "organizationName")]
    [InlineData("maya@gmail.com", "Acme", "", "subject")]
    public async Task RegisterExternal_ChecksTheRulesAgain_AndCreatesNothing(string email, string organization, string subject, string field)
    {
        var registration = new ExternalRegistration("google", subject, email, "Maya", organization, BusinessModel.Company);

        var exception = await AssertThrowsAsync<BadRequestException>(() => _service.RegisterExternalAsync(registration, CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
        await _accounts.DidNotReceiveWithAnyArgs().CreateAccountAsync(default!, default);
    }

    // --- LoginExternal -----------------------------------------------------------------------

    [Fact]
    public async Task LoginExternal_UsesTheMostRecentMembership_AndTouchesIt()
    {
        var recent = Guid.NewGuid();
        GivenMemberships(Membership(recent, OrganizationRole.MarketingManager), Membership(_agencyId, OrganizationRole.OrganizationAdmin));

        await _service.LoginExternalAsync(_userId, CancellationToken.None);

        _accessTokens.Received(1).Issue(_userId, recent, OrganizationRole.MarketingManager, Arg.Any<PermissionGrants>());
        await _memberships.Received(1).TouchAsync(recent, _userId, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginExternal_ForAUserWithNoOrganization_IsForbidden()
    {
        GivenMemberships();

        var exception = await AssertThrowsAsync<ForbiddenException>(() => _service.LoginExternalAsync(_userId, CancellationToken.None));

        Assert.Equal("no_agency", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }
}
