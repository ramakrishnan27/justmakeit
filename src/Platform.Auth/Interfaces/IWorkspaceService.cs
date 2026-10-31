using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

public interface IWorkspaceService
{
    /// <summary>The workspaces of the current agency.</summary>
    Task<IReadOnlyList<WorkspaceResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates a workspace in the current agency. Owners and admins only.</summary>
    Task<WorkspaceResponse> CreateAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken);
}
