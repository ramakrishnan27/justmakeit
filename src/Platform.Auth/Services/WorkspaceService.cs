using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

public sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
    IAgencyRepository agencies,
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
            throw new ForbiddenException("Only organization admins can create workspaces.", "insufficient_role");
        }

        // An individual creator has the one workspace made at sign-up. No role reaches this point for a
        // creator today; the rule is checked on its own so it still holds if the role rules change.
        var agency = await agencies.GetAsync(agencyId, userId, cancellationToken)
                     ?? throw new UnauthorizedException("You are no longer a member of this organization.", "membership_ended");
        if (!agency.BusinessModel.AllowsSeveralWorkspaces()
            && (await workspaces.ListAsync(agencyId, userId, cancellationToken)).Count > 0)
        {
            throw new ConflictException("An individual creator has one workspace.", "workspace_limit");
        }

        var workspace = new WorkspaceRecord
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            Name = request.Name!.Trim(),
            CreatedAt = time.GetUtcNow()
        };
        await workspaces.InsertAsync(workspace, userId, cancellationToken);

        logger.LogInformation("User {UserId} created workspace {WorkspaceId} in organization {AgencyId}", userId, workspace.Id, agencyId);
        return ToResponse(workspace);
    }

    private static WorkspaceResponse ToResponse(WorkspaceRecord workspace) =>
        new(workspace.Id, workspace.Name, workspace.CreatedAt);
}
