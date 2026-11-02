using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// The Site admin's editor for who holds which permission. A change raises the permission version, so everybody
/// who is signed in is asked to refresh once and then carries the new permissions.
/// </summary>
[ApiController]
[Route("v1/site/permissions")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.SiteAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class SitePermissionsController(IPermissionAdminService permissions) : ControllerBase
{
    /// <summary>Every permission, and for each one whether each business model and role holds it.</summary>
    [HttpGet]
    [ProducesResponseType<PermissionMatrixResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermissionMatrixResponse>> Get(CancellationToken cancellationToken) =>
        await permissions.GetMatrixAsync(cancellationToken);

    /// <summary>Grants or revokes one permission for one business model and role. A protected permission cannot be revoked.</summary>
    [HttpPut("{permissionKey}/grants")]
    [ProducesResponseType<PermissionGrantResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PermissionGrantResponse>> SetGrant(
        string permissionKey, SetGrantRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return await permissions.SetGrantAsync(permissionKey, request, cancellationToken);
    }
}
