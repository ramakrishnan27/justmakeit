using NSubstitute;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class SiteAdminDashboardServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly ISiteAdminRepository _siteAdmins = Substitute.For<ISiteAdminRepository>();
    private readonly TenantContext _tenant = new();
    private readonly SiteAdminDashboardService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public SiteAdminDashboardServiceTests()
    {
        _service = new SiteAdminDashboardService(_siteAdmins, _tenant);
    }

    private void SignInAsSiteAdmin() => _tenant.Set(null, _userId, RoleNames.SiteAdmin);

    // --- Overview ----------------------------------------------------------------------------

    [Fact]
    public async Task Overview_MapsEveryTotal_AsAskedByTheSiteAdmin()
    {
        SignInAsSiteAdmin();
        _siteAdmins.GetOverviewAsync(_userId, Arg.Any<CancellationToken>()).Returns(new SiteOverviewRecord
        {
            AgencyCount = 5,
            TrialAgencyCount = 3,
            PaidAgencyCount = 2,
            UserCount = 11,
            WorkspaceCount = 8,
            MemberCount = 14
        });

        var overview = await _service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(new SiteOverviewResponse(5, 3, 2, 11, 8, 14), overview);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("clientmanager")]
    [InlineData("contentwriter")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Overview_ForAnyTokenThatIsNotASiteAdminToken_IsForbidden_BeforeTheDatabaseIsAsked(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetOverviewAsync(CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
        await _siteAdmins.DidNotReceiveWithAnyArgs().GetOverviewAsync(default, default);
    }

    [Fact]
    public async Task Overview_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetOverviewAsync(CancellationToken.None));
    }

    // --- Agencies ----------------------------------------------------------------------------

    [Fact]
    public async Task Agencies_ReturnsThePageWithItsPagingDetails()
    {
        SignInAsSiteAdmin();
        var first = new SiteAgencyRecord
        {
            Id = Guid.NewGuid(), Name = "Acme", BusinessModel = BusinessModel.Agency, Plan = AgencyPlan.Paid, CreatedAt = Noon, MemberCount = 4, WorkspaceCount = 2, IsActive = true, ChannelCount = 3, TotalCount = 45
        };
        var second = new SiteAgencyRecord
        {
            Id = Guid.NewGuid(), Name = "Bright", BusinessModel = BusinessModel.Creator, Plan = AgencyPlan.Trial, CreatedAt = Noon.AddDays(-1), MemberCount = 1, WorkspaceCount = 1, IsActive = false, ChannelCount = 0, TotalCount = 45
        };
        _siteAdmins.ListAgenciesAsync(_userId, 3, 20, Arg.Any<CancellationToken>())
            .Returns(new SiteAgencyPage([first, second], 45));

        var page = await _service.ListAgenciesAsync(new SiteAgenciesQuery(3, 20), CancellationToken.None);

        Assert.Equal(3, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(45, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(
            [new SiteAgencyResponse(first.Id, "Acme", BusinessModel.Agency, AgencyPlan.Paid, Noon, 4, 2, true, 3),
             new SiteAgencyResponse(second.Id, "Bright", BusinessModel.Creator, AgencyPlan.Trial, Noon.AddDays(-1), 1, 1, false, 0)],
            page.Items);
    }

    [Fact]
    public async Task Agencies_UsesTheDefaultPage_WhenNoneIsGiven()
    {
        SignInAsSiteAdmin();
        _siteAdmins.ListAgenciesAsync(_userId, 1, 20, Arg.Any<CancellationToken>()).Returns(new SiteAgencyPage([], 0));

        var page = await _service.ListAgenciesAsync(new SiteAgenciesQuery(), CancellationToken.None);

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    [InlineData(1, -5, "pageSize")]
    public async Task Agencies_WithABadPage_IsABadRequest_AndAsksNothing(int page, int pageSize, string field)
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ListAgenciesAsync(new SiteAgenciesQuery(page, pageSize), CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
        await _siteAdmins.DidNotReceiveWithAnyArgs().ListAgenciesAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("marketingmanager")]
    [InlineData(null)]
    public async Task Agencies_ForAnyTokenThatIsNotASiteAdminToken_IsForbidden(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListAgenciesAsync(new SiteAgenciesQuery(), CancellationToken.None));

        await _siteAdmins.DidNotReceiveWithAnyArgs().ListAgenciesAsync(default, default, default, default);
    }

    [Fact]
    public async Task Agencies_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAgenciesAsync(new SiteAgenciesQuery(), CancellationToken.None));
    }

    // --- Members -----------------------------------------------------------------------------

    private static SiteMemberRecord Member(
        string name, string agencyName, BusinessModel model, OrganizationRole role, bool isActive, DateTimeOffset joinedAt, long total) =>
        new()
        {
            UserId = Guid.NewGuid(),
            FullName = name,
            Email = $"{name.ToLowerInvariant()}@example.com",
            AgencyId = Guid.NewGuid(),
            AgencyName = agencyName,
            BusinessModel = model,
            Role = role,
            IsActive = isActive,
            JoinedAt = joinedAt,
            TotalCount = total
        };

    [Fact]
    public async Task Members_ReturnsThePageWithItsPagingDetails_AndEveryFieldOfEachMembership()
    {
        SignInAsSiteAdmin();
        var first = Member("Ana", "Acme", BusinessModel.Agency, OrganizationRole.ClientManager, true, Noon, 31);
        var second = Member("Bo", "Bright", BusinessModel.Creator, OrganizationRole.ContentWriter, false, Noon.AddDays(-2), 31);
        _siteAdmins.ListMembersAsync(_userId, 2, 15, Arg.Any<CancellationToken>())
            .Returns(new SiteMemberPage([first, second], 31));

        var page = await _service.ListMembersAsync(new SiteMembersQuery(2, 15), CancellationToken.None);

        Assert.Equal(2, page.Page);
        Assert.Equal(15, page.PageSize);
        Assert.Equal(31, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(
            [new SiteMemberResponse(first.UserId, "Ana", "ana@example.com", first.AgencyId, "Acme", BusinessModel.Agency, OrganizationRole.ClientManager, true, Noon),
             new SiteMemberResponse(second.UserId, "Bo", "bo@example.com", second.AgencyId, "Bright", BusinessModel.Creator, OrganizationRole.ContentWriter, false, Noon.AddDays(-2))],
            page.Items);
    }

    [Fact]
    public async Task Members_UsesTheDefaultPage_WhenNoneIsGiven()
    {
        SignInAsSiteAdmin();
        _siteAdmins.ListMembersAsync(_userId, 1, 20, Arg.Any<CancellationToken>()).Returns(new SiteMemberPage([], 0));

        var page = await _service.ListMembersAsync(new SiteMembersQuery(), CancellationToken.None);

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    [InlineData(1, -5, "pageSize")]
    public async Task Members_WithABadPage_IsABadRequest_AndAsksNothing(int page, int pageSize, string field)
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ListMembersAsync(new SiteMembersQuery(page, pageSize), CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
        await _siteAdmins.DidNotReceiveWithAnyArgs().ListMembersAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("marketingmanager")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Members_ForAnyTokenThatIsNotASiteAdminToken_IsForbidden_BeforeTheDatabaseIsAsked(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListMembersAsync(new SiteMembersQuery(), CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
        await _siteAdmins.DidNotReceiveWithAnyArgs().ListMembersAsync(default, default, default, default);
    }

    [Fact]
    public async Task Members_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListMembersAsync(new SiteMembersQuery(), CancellationToken.None));
    }

    [Fact]
    public void MembersQuery_HasTheSameLimitsAsTheAgencyList()
    {
        Assert.Equal(SiteAgenciesQuery.DefaultPageSize, new SiteMembersQuery().PageSize);
        Assert.False(new SiteMembersQuery(1, SiteAgenciesQuery.MaxPageSize).Validate().Any);
        Assert.True(new SiteMembersQuery(1, SiteAgenciesQuery.MaxPageSize + 1).Validate().Any);
    }
}
