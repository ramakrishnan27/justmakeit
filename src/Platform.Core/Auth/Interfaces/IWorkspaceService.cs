using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

public interface IWorkspaceService
{
    /// <summary>The workspaces of the current organization.</summary>
    Task<IReadOnlyList<WorkspaceResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a workspace in the current organization. Organization admins only; an individual creator
    /// already has their one workspace, so a second is a conflict.
    /// </summary>
    Task<WorkspaceResponse> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken);
}
