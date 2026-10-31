using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IWorkspaceRepository
{
    Task<IReadOnlyList<WorkspaceRecord>> ListAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);

    Task InsertAsync(WorkspaceRecord workspace, Guid userId, CancellationToken cancellationToken);
}
