using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IPermissionRepository
{
    /// <summary>
    /// Reads every grant and the permission version in one statement. Needs no signed-in user, because a
    /// token is issued while a person is still signing in.
    /// </summary>
    Task<PermissionSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>The catalog and every grant, for the Site admin's screen. Throws <c>not_site_admin</c> (403) for anybody else.</summary>
    Task<PermissionCatalog> GetCatalogAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Grants or revokes one permission for one business model and role. Returns false when there is no such
    /// slot or permission. Throws <c>permission_protected</c> (409) for an attempt to revoke a protected one,
    /// and <c>not_site_admin</c> (403) for anybody who is not a site admin.
    /// </summary>
    Task<bool> SetGrantAsync(
        Guid userId, string businessModel, string role, string permissionKey, bool granted, CancellationToken cancellationToken);
}
