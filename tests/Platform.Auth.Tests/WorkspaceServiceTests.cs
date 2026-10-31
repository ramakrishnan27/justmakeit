using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class WorkspaceServiceTests
{
    private readonly IWorkspaceRepository _workspaces = Substitute.For<IWorkspaceRepository>();
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly WorkspaceService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public WorkspaceServiceTests()
    {
        _service = new WorkspaceService(_workspaces, _tenant, _time, NullLogger<WorkspaceService>.Instance);
    }

    // --- List --------------------------------------------------------------------------------

    [Fact]
    public async Task List_AsksOnlyForTheAgencyInTheTenantContext()
    {
        _tenant.Set(_agencyId, _userId, "viewer");
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
    [InlineData("owner")]
    [InlineData("admin")]
    public async Task Create_AsOwnerOrAdmin_StoresTheWorkspaceInTheCallersAgency(string role)
    {
        _tenant.Set(_agencyId, _userId, role);
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
    [InlineData("manager")]
    [InlineData("viewer")]
    [InlineData("Owner")]
    [InlineData("0")]
    [InlineData("superuser")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Create_WithoutAnOwnerOrAdminRole_IsForbidden_AndStoresNothing(string? role)
    {
        _tenant.Set(_agencyId, _userId, role);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest("Client one"), CancellationToken.None));

        Assert.Equal("insufficient_role", exception.Code);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_IsABadRequest_AndStoresNothing(string? name)
    {
        _tenant.Set(_agencyId, _userId, "owner");

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(new CreateWorkspaceRequest(name), CancellationToken.None));

        Assert.Contains("name", exception.Errors!.Keys);
        await _workspaces.DidNotReceiveWithAnyArgs().InsertAsync(default!, default, default);
    }

    [Fact]
    public async Task Create_WithATooLongName_IsABadRequest()
    {
        _tenant.Set(_agencyId, _userId, "owner");

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
