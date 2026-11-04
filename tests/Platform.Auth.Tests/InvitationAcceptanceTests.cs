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
using Platform.Helpers.Utilities;

namespace Platform.Auth.Tests;

/// <summary>Previewing and accepting an invitation, in <see cref="AuthService"/> where the member's access token is made.</summary>
public sealed class InvitationAcceptanceTests
{
    private const string Token = "T0ken-From-The-Link_0123456789abcdefghijklmnopq";
    private const string Password = "a long enough password";

    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly AuthService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly PermissionGrants _grants = new(9, ["dashboard.view", "workspaces.view"]);

    public InvitationAcceptanceTests()
    {
        _accessTokens.Issue(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<OrganizationRole>(), Arg.Any<PermissionGrants>())
            .Returns(_ => new AccessToken("access-token", _time.Now.AddDays(30)));
        _permissions.GetGrantsAsync(Arg.Any<BusinessModel>(), Arg.Any<OrganizationRole>(), Arg.Any<CancellationToken>()).Returns(_grants);

        _service = new AuthService(
            Substitute.For<IAccountRepository>(),
            Substitute.For<IMembershipRepository>(),
            Substitute.For<IAgencyRepository>(),
            _invitations,
            _hasher,
            _accessTokens,
            _permissions,
            new TenantContext(),
            _time,
            NullLogger<AuthService>.Instance);
    }

    private InvitationPreviewRecord Pending(OrganizationRole role = OrganizationRole.MarketingManager) => new()
    {
        Id = Guid.NewGuid(),
        AgencyId = _agencyId,
        OrganizationName = "Acme",
        BusinessModel = BusinessModel.Company,
        Email = "new@example.com",
        Role = role,
        ExpiresAt = _time.Now.AddDays(5),
        FullName = "Dev Kumar"
    };

    private void GivenPending(InvitationPreviewRecord? invitation) =>
        _invitations.FindAsync(HashHelper.Sha256Hex(Token), Arg.Any<CancellationToken>()).Returns(invitation);

    // --- Preview -----------------------------------------------------------------------------

    [Fact]
    public async Task Preview_ShowsWhereTheLinkLeadsAndForWhichAddress_AndTheNameTheAdminTyped()
    {
        var pending = Pending(OrganizationRole.ContentWriter);
        GivenPending(pending);

        var preview = await _service.PreviewInvitationAsync(new InvitationTokenRequest(Token), CancellationToken.None);

        Assert.Equal(
            new InvitationPreviewResponse("Acme", BusinessModel.Company, OrganizationRole.ContentWriter, "new@example.com", pending.ExpiresAt, "Dev Kumar"),
            preview);
    }

    [Fact]
    public async Task Preview_LooksTheTokenUpByItsHash_AndIgnoresSurroundingSpace()
    {
        GivenPending(Pending());

        await _service.PreviewInvitationAsync(new InvitationTokenRequest($"  {Token}\n"), CancellationToken.None);

        await _invitations.Received(1).FindAsync(HashHelper.Sha256Hex(Token), Arg.Any<CancellationToken>());
        await _invitations.DidNotReceive().FindAsync(Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_OfALinkThatIsNotPending_IsTheSame404ForEveryReason()
    {
        GivenPending(null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.PreviewInvitationAsync(new InvitationTokenRequest(Token), CancellationToken.None));

        Assert.Equal("invitation_invalid", exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Preview_WithoutAToken_IsABadRequest_AndLooksNothingUp(string? token)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.PreviewInvitationAsync(new InvitationTokenRequest(token), CancellationToken.None));

        Assert.Contains("token", exception.Errors!.Keys);
        Assert.Empty(_invitations.ReceivedCalls());
    }

    // --- Accept ------------------------------------------------------------------------------

    [Fact]
    public async Task Accept_CreatesThePerson_AndSignsThemInAsTheInvitedRole()
    {
        var pending = Pending(OrganizationRole.MarketingManager);
        GivenPending(pending);
        Guid createdUser = default;
        string? storedHash = null;
        _invitations
            .AcceptAsync(Arg.Any<string>(), Arg.Do<Guid>(id => createdUser = id), Arg.Any<string>(), Arg.Do<string>(h => storedHash = h), Arg.Any<CancellationToken>())
            .Returns(new AcceptedInvitationRecord { AgencyId = _agencyId, Role = OrganizationRole.MarketingManager });

        var result = await _service.AcceptInvitationAsync(
            new AcceptInvitationRequest(Token, "  Ana Smith ", Password), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.NotEqual(Guid.Empty, createdUser);
        await _invitations.Received(1).AcceptAsync(
            HashHelper.Sha256Hex(Token), createdUser, "Ana Smith", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _accessTokens.Received(1).Issue(createdUser, _agencyId, OrganizationRole.MarketingManager, _grants);

        // The password is stored hashed, never as typed.
        Assert.NotNull(storedHash);
        Assert.NotEqual(Password, storedHash);
        Assert.True(_hasher.Verify(Password, storedHash));
    }

    [Fact]
    public async Task Accept_ReadsThePermissionsOfTheInvitedRole_BeforeAnythingIsWritten()
    {
        GivenPending(Pending(OrganizationRole.ContentWriter));
        _invitations.AcceptAsync(default!, default, default!, default!, default)
            .ReturnsForAnyArgs(new AcceptedInvitationRecord { AgencyId = _agencyId, Role = OrganizationRole.ContentWriter });

        await _service.AcceptInvitationAsync(new AcceptInvitationRequest(Token, "Ana", Password), CancellationToken.None);

        Received.InOrder(() =>
        {
            _permissions.GetGrantsAsync(BusinessModel.Company, OrganizationRole.ContentWriter, Arg.Any<CancellationToken>());
            _invitations.AcceptAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Accept_OfALinkThatIsNotPending_IsInvalid_AndCreatesNothing()
    {
        GivenPending(null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.AcceptInvitationAsync(new AcceptInvitationRequest(Token, "Ana", Password), CancellationToken.None));

        Assert.Equal("invitation_invalid", exception.Code);
        await _invitations.DidNotReceiveWithAnyArgs().AcceptAsync(default!, default, default!, default!, default);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Accept_WhenTheAddressBecameAnAccountInTheMeantime_IsAConflict_AndNoTokenIsIssued()
    {
        GivenPending(Pending());
        _invitations.AcceptAsync(default!, default, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new ConflictException("An account with this email already exists.", "email_taken"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.AcceptInvitationAsync(new AcceptInvitationRequest(Token, "Ana", Password), CancellationToken.None));

        Assert.Equal("email_taken", exception.Code);
        _accessTokens.DidNotReceiveWithAnyArgs().Issue(default, default, default, default!);
    }

    [Fact]
    public async Task Accept_WhenTheInvitationWasUsedBetweenTheLookupAndTheWrite_IsInvalid()
    {
        GivenPending(Pending());
        _invitations.AcceptAsync(default!, default, default!, default!, default)
            .ThrowsAsyncForAnyArgs(InvitationErrors.Invalid());

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.AcceptInvitationAsync(new AcceptInvitationRequest(Token, "Ana", Password), CancellationToken.None));

        Assert.Equal("invitation_invalid", exception.Code);
    }

    [Theory]
    [InlineData(null, "Ana", Password, "token")]
    [InlineData(Token, null, Password, "fullName")]
    [InlineData(Token, "   ", Password, "fullName")]
    [InlineData(Token, "Ana", null, "password")]
    [InlineData(Token, "Ana", "short", "password")]
    public async Task Accept_WithABadForm_ReportsTheField_AndTouchesNothing(string? token, string? name, string? password, string field)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.AcceptInvitationAsync(new AcceptInvitationRequest(token, name, password), CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
        Assert.Empty(_invitations.ReceivedCalls());
    }

    [Fact]
    public void TheRequestHasNoFieldForTheEmail_SoTheAddressIsAlwaysTheInvitedOne()
    {
        var properties = typeof(AcceptInvitationRequest).GetProperties().Select(p => p.Name).Order();

        Assert.Equal(["FullName", "Password", "Token"], properties);
    }
}
