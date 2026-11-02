using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Tests;

public sealed class PermissionAdminServiceTests
{
    private readonly IPermissionRepository _permissions = Substitute.For<IPermissionRepository>();
    private readonly IPermissionService _cache = Substitute.For<IPermissionService>();
    private readonly TenantContext _tenant = new();
    private readonly PermissionAdminService _service;

    private readonly Guid _userId = Guid.NewGuid();

    public PermissionAdminServiceTests()
    {
        _service = new PermissionAdminService(_permissions, _cache, _tenant, NullLogger<PermissionAdminService>.Instance);
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);
        _permissions.SetGrantAsync(_userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private static SetGrantRequest Request(string model = "company", string role = "organizationadmin", bool granted = false) =>
        new(model, role, granted);

    // --- Who may ------------------------------------------------------------------------------

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("contentwriter")]
    [InlineData(null)]
    public async Task EveryOperation_IsForbidden_ToAnyoneWhoIsNotASiteAdmin_AndTouchesNothing(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role, PermissionKeys.All);

        var matrix = await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetMatrixAsync(CancellationToken.None));
        var grant = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(), CancellationToken.None));

        Assert.Equal("not_site_admin", matrix.Code);
        Assert.Equal("not_site_admin", grant.Code);
        Assert.Empty(_permissions.ReceivedCalls());
        _cache.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task WithoutAUser_EveryOperationIsUnauthorized()
    {
        _tenant.Clear();

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMatrixAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(), CancellationToken.None));
    }

    // --- The matrix ---------------------------------------------------------------------------

    private static PermissionCatalog Catalog() => new(
        [
            new PermissionRecord { Key = PermissionKeys.DashboardView, Title = "View the dashboard", Description = "Home.", IsProtected = true },
            new PermissionRecord { Key = PermissionKeys.WorkspacesCreate, Title = "Create workspaces", Description = "Add one.", IsProtected = false }
        ],
        [
            new GrantRecord { BusinessModel = "company", Role = "organizationadmin", PermissionKey = PermissionKeys.WorkspacesCreate, IsGranted = true },
            new GrantRecord { BusinessModel = "agency", Role = "organizationadmin", PermissionKey = PermissionKeys.WorkspacesCreate, IsGranted = true },
            new GrantRecord { BusinessModel = "creator", Role = "contentwriter", PermissionKey = PermissionKeys.WorkspacesCreate, IsGranted = false },
            new GrantRecord { BusinessModel = "company", Role = "organizationadmin", PermissionKey = PermissionKeys.DashboardView, IsGranted = true }
        ]);

    [Fact]
    public async Task TheMatrix_HasTheEightSlotsInAFixedOrder()
    {
        _permissions.GetCatalogAsync(_userId, Arg.Any<CancellationToken>()).Returns(Catalog());

        var matrix = await _service.GetMatrixAsync(CancellationToken.None);

        Assert.Equal(
            [
                "company/organizationadmin", "company/marketingmanager", "company/contentwriter",
                "agency/organizationadmin", "agency/clientmanager", "agency/marketingmanager", "agency/contentwriter",
                "creator/contentwriter"
            ],
            matrix.Slots.Select(s => $"{s.BusinessModel}/{s.Role}"));
    }

    [Fact]
    public async Task TheMatrix_GivesEveryPermissionAGrantForEverySlot_AndAMissingRowMeansNotGranted()
    {
        _permissions.GetCatalogAsync(_userId, Arg.Any<CancellationToken>()).Returns(Catalog());

        var matrix = await _service.GetMatrixAsync(CancellationToken.None);

        Assert.Equal([PermissionKeys.DashboardView, PermissionKeys.WorkspacesCreate], matrix.Permissions.Select(p => p.Key));
        Assert.All(matrix.Permissions, p => Assert.Equal(8, p.Grants.Count));
        var create = matrix.Permissions.Single(p => p.Key == PermissionKeys.WorkspacesCreate);
        Assert.Equal(
            ["company/organizationadmin", "agency/organizationadmin"],
            create.Grants.Where(g => g.Granted).Select(g => $"{g.BusinessModel}/{g.Role}"));
        // Only one dashboard row exists in the fixture; the other seven slots read as not granted.
        Assert.Equal(1, matrix.Permissions.Single(p => p.Key == PermissionKeys.DashboardView).Grants.Count(g => g.Granted));
    }

    [Fact]
    public async Task TheMatrix_CarriesTheTitleDescriptionAndWhetherItIsProtected()
    {
        _permissions.GetCatalogAsync(_userId, Arg.Any<CancellationToken>()).Returns(Catalog());

        var matrix = await _service.GetMatrixAsync(CancellationToken.None);

        var dashboard = matrix.Permissions.Single(p => p.Key == PermissionKeys.DashboardView);
        Assert.Equal(("View the dashboard", "Home.", true), (dashboard.Title, dashboard.Description, dashboard.IsProtected));
        Assert.False(matrix.Permissions.Single(p => p.Key == PermissionKeys.WorkspacesCreate).IsProtected);
    }

    // --- Setting a grant ----------------------------------------------------------------------

    [Fact]
    public async Task SetGrant_StoresTheStoredNames_ForgetsThePermissionCache_AndReportsWhatItDid()
    {
        var response = await _service.SetGrantAsync(
            PermissionKeys.WorkspacesCreate, Request("agency", "organizationadmin", granted: false), CancellationToken.None);

        await _permissions.Received(1).SetGrantAsync(
            _userId, "agency", "organizationadmin", PermissionKeys.WorkspacesCreate, false, Arg.Any<CancellationToken>());
        _cache.Received(1).Invalidate();
        Assert.Equal(new PermissionGrantResponse(PermissionKeys.WorkspacesCreate, "agency", "organizationadmin", false), response);
    }

    [Fact]
    public async Task SetGrant_CanGrantAsWellAsRevoke()
    {
        var response = await _service.SetGrantAsync(
            PermissionKeys.WorkspacesCreate, Request("company", "contentwriter", granted: true), CancellationToken.None);

        Assert.True(response.Granted);
        await _permissions.Received(1).SetGrantAsync(
            _userId, "company", "contentwriter", PermissionKeys.WorkspacesCreate, true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("creator", "organizationadmin")]
    [InlineData("creator", "marketingmanager")]
    [InlineData("company", "clientmanager")]
    public async Task SetGrant_ForARoleThatDoesNotExistInThatKindOfOrganization_IsABadRequest_AndChangesNothing(string model, string role)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(model, role), CancellationToken.None));

        Assert.Equal("invalid_slot", exception.Code);
        Assert.Empty(_permissions.ReceivedCalls());
        _cache.DidNotReceive().Invalidate();
    }

    [Theory]
    [InlineData("nope.nothing")]
    [InlineData("workspaces.crate")]
    [InlineData("")]
    [InlineData("Workspaces.Create")]
    public async Task SetGrant_ForAPermissionThatDoesNotExist_IsNotFound_AndChangesNothing(string key)
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.SetGrantAsync(key, Request(), CancellationToken.None));

        Assert.Equal("permission_not_found", exception.Code);
        Assert.Empty(_permissions.ReceivedCalls());
    }

    [Fact]
    public async Task SetGrant_RevokingAProtectedPermission_IsAConflict_WithoutAskingTheDatabase()
    {
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.SetGrantAsync(PermissionKeys.DashboardView, Request("agency", "clientmanager", granted: false), CancellationToken.None));

        Assert.Equal("permission_protected", exception.Code);
        Assert.Empty(_permissions.ReceivedCalls());
        _cache.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task SetGrant_GrantingAProtectedPermission_IsAllowed_ItJustChangesNothing()
    {
        await _service.SetGrantAsync(PermissionKeys.DashboardView, Request(granted: true), CancellationToken.None);

        await _permissions.Received(1).SetGrantAsync(
            _userId, "company", "organizationadmin", PermissionKeys.DashboardView, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetGrant_WhenTheDatabaseKnowsNoSuchRow_IsNotFound_AndTheCacheIsStillForgotten()
    {
        _permissions.SetGrantAsync(_userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(), CancellationToken.None));

        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task SetGrant_WhenTheDatabaseRefuses_TheErrorReachesTheCaller_AndTheCacheIsStillForgotten()
    {
        _permissions.SetGrantAsync(_userId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ForbiddenException("Site admin access is required.", "not_site_admin"));

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(), CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
        _cache.Received(1).Invalidate();
    }

    [Theory]
    [InlineData(null, "organizationadmin", true, "businessModel")]
    [InlineData("holding", "organizationadmin", true, "businessModel")]
    [InlineData("company", null, true, "role")]
    [InlineData("company", "owner", true, "role")]
    [InlineData("company", "organizationadmin", null, "granted")]
    public async Task SetGrant_WithABadField_IsABadRequest_NamingIt(string? model, string? role, bool? granted, string field)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, new SetGrantRequest(model, role, granted), CancellationToken.None));

        Assert.Equal("validation_failed", exception.Code);
        Assert.Contains(field, exception.Errors!.Keys);
        Assert.Empty(_permissions.ReceivedCalls());
    }

    [Fact]
    public async Task EverySlotThatBusinessModelRulesAllows_IsAcceptedByTheService()
    {
        foreach (var model in Enum.GetValues<BusinessModel>())
        {
            foreach (var role in model.AllowedRoles())
            {
                await _service.SetGrantAsync(PermissionKeys.WorkspacesCreate, Request(model.ToDb(), role.ToDb(), true), CancellationToken.None);
            }
        }

        await _permissions.Received(8).SetGrantAsync(
            _userId, Arg.Any<string>(), Arg.Any<string>(), PermissionKeys.WorkspacesCreate, true, Arg.Any<CancellationToken>());
    }
}
