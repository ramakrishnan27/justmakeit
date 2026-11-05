using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Interfaces;

/// <summary>
/// The permissions of each business model and role, and the version of them, held in memory. Tokens are
/// issued from it and every request is compared with it, so no request reads the database for permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>The permission keys this slot holds right now, with the version they were read under.</summary>
    Task<PermissionGrants> GetGrantsAsync(BusinessModel businessModel, OrganizationRole role, CancellationToken cancellationToken);

    /// <summary>The current permission version. A token issued under any other version is out of date.</summary>
    Task<long> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>Forgets what is held, so the next call reads the database. Call it after a grant changed.</summary>
    void Invalidate();
}
