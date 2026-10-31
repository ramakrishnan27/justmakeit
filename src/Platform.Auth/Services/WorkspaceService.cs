using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

public sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
    ITenantContext tenant,
    TimeProvider time,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    public async Task<IReadOnlyList<WorkspaceResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await workspaces.ListAsync(tenant.RequireAgencyId(), tenant.RequireUserId(), cancellationToken);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<WorkspaceResponse> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var agencyId = tenant.RequireAgencyId();
        var userId = tenant.RequireUserId();

        // The CanManage policy already stopped everyone else; this is the same rule, checked again
        // here so no other caller of the service can skip it.
        if (!RolePermissions.TryParse(tenant.Role, out var role) || !role.CanManage())
        {
            throw new ForbiddenException("Only owners and admins can create workspaces.", "insufficient_role");
        }

        var workspace = new WorkspaceRecord
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            Name = request.Name!.Trim(),
            CreatedAt = time.GetUtcNow()
        };
        await workspaces.InsertAsync(workspace, userId, cancellationToken);

        logger.LogInformation("User {UserId} created workspace {WorkspaceId} in agency {AgencyId}", userId, workspace.Id, agencyId);
        return ToResponse(workspace);
    }

    private static WorkspaceResponse ToResponse(WorkspaceRecord workspace) =>
        new(workspace.Id, workspace.Name, workspace.CreatedAt);
}
