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

public sealed class WorkspaceServiceTests
{
    private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
    private readonly IAgencyRepository _agencies = Substitute.For<IAgencyRepository>();
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly WorkspaceService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public WorkspaceServiceTests()
    {
        GivenBusinessModel(BusinessModel.Agency);
        _service = new WorkspaceService(_workspaces, _agencies, _tenant, _time, NullLogger<WorkspaceService>.Instance);
    }

    private void GivenBusinessModel(BusinessModel model) =>
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>())
            .Returns(new AgencyRecord { Id = _agencyId, Name = "Acme", BusinessModel = model, Plan = AgencyPlan.Trial });

    // --- List --------------------------------------------------------------------------------

    [Fact]
    public async Task List_AsksOnlyForTheAgencyInTheTenantContext()
    {
        _tenant.Set(_agencyId, _userId, "clientmanager", [PermissionKeys.WorkspacesView]);
        _workspaces.ListAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(
        [
            new WorkspaceRecord { Id = Guid.NewGuid(), AgencyId = _agencyId, Name = "One", CreatedAt = _time.Now },
            new WorkspaceRecord { Id = Guid.NewGuid(), AgencyId = _agencyId, Name = "Two", CreatedAt = _time.Now.AddMinutes(1) }
        ]);

        var result = await _service.ListAsync(CancellationToken.None);

        Assert.Equal(["One", "Two"], result.Select(w => w.Name));
        await _workspaces.Received(1).ListAsync(_agencyId, _userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithoutAnAgencyInTheTenantContext_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAsync(CancellationToken.None));

        await _workspaces.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    // --- Create ------------------------------------------------------------------------------

    [Theory]
    [InlineData(BusinessModel.Company)]
    [InlineData(BusinessModel.Agency)]
    public async Task Create_AsAnOrganizationAdmin_StoresTheWorkspaceInTheCallersOrganization(BusinessModel model)
    {
        GivenBusinessModel(model);
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);
        WorkspaceRecord? stored = null;
        await _workspaces.InsertAsync(Arg.Do<WorkspaceRecord>(w => stored = w), _userId, Arg.Any<CancellationToken>());

        var created = await _service.CreateAsync(new CreateWorkspaceRequest("  Client one  "), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(_agencyId, stored.AgencyId);
        Assert.Equal("Client one", stored.Name);
        Assert.Equal(_time.Now, stored.CreatedAt);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(new WorkspaceResponse(stored.Id, "Client one", _time.Now), created);
    }

    [Theory]
    [InlineData("organizationadmin")] // the role that holds the permission by default, here without it
    [InlineData("marketingmanager")]
    [InlineData("contentwriter")]
    [InlineData("clientmanager")]
    [InlineData(null)]
    public async Task Create_WithoutTheCreatePermission_IsForbidden_WhateverTheRole_AndStoresNothing(string? role)
    {
        // Everything else the catalog has, including the baseline that used to stand for "manage".
        var others = PermissionKeys.All.Where(key => key != PermissionKeys.WorkspacesCreate);
        _tenant.Set(_agencyId, _userId, role, others);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None));

        Assert.Equal("insufficient_permission", exception.Code);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task Create_WithNoPermissionsAtAll_IsForbidden()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin");

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None));

        Assert.Equal("insufficient_permission", exception.Code);
    }

    [Fact]
    public async Task Create_ForARoleTheSiteAdminGaveThePermission_IsAllowed()
    {
        // The permission, not the role, decides: a marketing manager can create workspaces once granted it.
        _tenant.Set(_agencyId, _userId, "marketingmanager", [PermissionKeys.WorkspacesCreate]);

        await _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None);

        await _workspaces.Received(1).InsertAsync(Arg.Any<WorkspaceRecord>(), _userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithoutTheViewPermission_IsForbidden_AndReadsNothing()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate, PermissionKeys.DataRead]);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListAsync(CancellationToken.None));

        Assert.Equal("insufficient_permission", exception.Code);
        await _workspaces.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task Create_ForAnIndividualCreatorWhoHasTheirWorkspace_IsAConflict_AndStoresNothing()
    {
        // No role reaches this for a creator today (they are content writers); the model rule stands on its own.
        GivenBusinessModel(BusinessModel.Creator);
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);
        _workspaces.ListAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(
            [new WorkspaceRecord { Id = Guid.NewGuid(), AgencyId = _agencyId, Name = "Main workspace", CreatedAt = _time.Now }]);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Second"), CancellationToken.None));

        Assert.Equal("workspace_limit", exception.Code);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task Create_ForAnIndividualCreatorWithoutAWorkspaceYet_IsAllowed()
    {
        GivenBusinessModel(BusinessModel.Creator);
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);
        _workspaces.ListAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns([]);

        await _service.CreateAsync(new CreateWorkspaceRequest("Main workspace"), CancellationToken.None);

        await _workspaces.Received(1).InsertAsync(Arg.Any<WorkspaceRecord>(), _userId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BusinessModel.Company)]
    [InlineData(BusinessModel.Agency)]
    public async Task Create_ForACompanyOrAnAgency_DoesNotLookAtHowManyWorkspacesThereAre(BusinessModel model)
    {
        GivenBusinessModel(model);
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);

        await _service.CreateAsync(new CreateWorkspaceRequest("Another client"), CancellationToken.None);

        await _workspaces.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task Create_WhenTheOrganizationCannotBeSeen_IsUnauthorized_AndStoresNothing()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns((AgencyRecord?)null);

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None));

        Assert.Equal("membership_ended", exception.Code);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_IsABadRequest_AndStoresNothing(string? name)
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest(name), CancellationToken.None));

        Assert.Contains("name", exception.Errors!.Keys);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task Create_WithATooLongName_IsABadRequest()
    {
        _tenant.Set(_agencyId, _userId, "organizationadmin", [PermissionKeys.WorkspacesCreate]);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest(new string('n', 201)), CancellationToken.None));
    }

    [Fact]
    public async Task Create_WithoutASignedInUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None));
    }
}
