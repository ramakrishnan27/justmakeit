using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class SiteOrganizationServiceTests
{
    private readonly ISiteAdminRepository _siteAdmins = Substitute.For<ISiteAdminRepository>();
    private readonly ISessionRevocationService _revocations = Substitute.For<ISessionRevocationService>();
    private readonly TenantContext _tenant = new();
    private readonly SiteOrganizationService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();

    public SiteOrganizationServiceTests()
    {
        _service = new SiteOrganizationService(_siteAdmins, _revocations, _tenant, NullLogger<SiteOrganizationService>.Instance);
    }

    private void SignInAsSiteAdmin() => _tenant.Set(null, _userId, RoleNames.SiteAdmin);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingTheStatus_PassesTheChoiceToTheDatabase_AndMakesTheApiForgetWhatItHeldAboutEndedSessions(bool isActive)
    {
        SignInAsSiteAdmin();
        _siteAdmins.SetOrganizationActiveAsync(_userId, _agencyId, isActive, Arg.Any<CancellationToken>()).Returns(true);

        await _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(isActive), CancellationToken.None);

        await _siteAdmins.Received(1).SetOrganizationActiveAsync(_userId, _agencyId, isActive, Arg.Any<CancellationToken>());
        _revocations.Received(1).Invalidate();
    }

    [Fact]
    public async Task WhenTheDatabaseCallFails_TheApiStillForgetsWhatItHeld()
    {
        // The sessions may have ended in the database even though the call failed after that, so the list must be read again.
        SignInAsSiteAdmin();
        _siteAdmins.SetOrganizationActiveAsync(_userId, _agencyId, false, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("connection lost"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(false), CancellationToken.None));

        _revocations.Received(1).Invalidate();
    }

    [Fact]
    public async Task AnUnknownOrganization_IsNotFound()
    {
        SignInAsSiteAdmin();
        _siteAdmins.SetOrganizationActiveAsync(_userId, _agencyId, false, Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotFoundException("That organization does not exist.", "organization_not_found"));

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(false), CancellationToken.None));

        Assert.Equal("organization_not_found", exception.Code);
    }

    [Fact]
    public async Task WithoutSayingActiveOrNot_IsABadRequest_AndChangesNothing()
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(null), CancellationToken.None));

        Assert.Contains("isActive", exception.Errors!.Keys);
        await _siteAdmins.DidNotReceiveWithAnyArgs().SetOrganizationActiveAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("marketingmanager")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ForAnyTokenThatIsNotASiteAdminToken_IsForbidden_BeforeTheDatabaseIsAsked(string? role)
    {
        _tenant.Set(_agencyId, _userId, role);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(false), CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
        await _siteAdmins.DidNotReceiveWithAnyArgs().SetOrganizationActiveAsync(default, default, default, default);
    }

    [Fact]
    public async Task WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.SetActiveAsync(_agencyId, new SetOrganizationActiveRequest(false), CancellationToken.None));
    }
}
