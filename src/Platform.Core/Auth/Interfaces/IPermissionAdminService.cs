using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

/// <summary>What the Site admin does to the permissions. Every method needs a site admin and answers 403 <c>not_site_admin</c> otherwise.</summary>
public interface IPermissionAdminService
{
    Task<PermissionMatrixResponse> GetMatrixAsync(CancellationToken cancellationToken);

    Task<PermissionGrantResponse> SetGrantAsync(string permissionKey, SetGrantRequest request, CancellationToken cancellationToken);
}
