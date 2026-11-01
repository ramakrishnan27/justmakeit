using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>The workspaces of the current organization (one per client in an agency), for the web app's workspace switcher.</summary>
[ApiController]
[Route("v1/workspaces")]
[Produces("application/json")]
public sealed class WorkspacesController(IWorkspaceService workspaces) : ControllerBase
{
    /// <summary>Lists the workspaces of the current organization. Any member may read them.</summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.CanRead)]
    [ProducesResponseType<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await workspaces.ListAsync(cancellationToken));

    /// <summary>
    /// Creates a workspace in the current organization. Organization admins only, and never in an
    /// individual creator's organization, which has the one workspace made at sign-up.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PolicyNames.CanManage)]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var created = await workspaces.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}
