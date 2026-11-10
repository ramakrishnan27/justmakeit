using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Content.DTOs;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Content.Features.Templates;

/// <summary>Templates: reusable starting points for a post. Reading needs <c>contentstudio.view</c>; saving and deleting <c>contentstudio.create</c>.</summary>
[ApiController]
[Route("v1/content/templates")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TemplatesController(ITemplateService templates) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<IReadOnlyList<TemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TemplateResponse>>> List([FromQuery] TemplateListQuery query, CancellationToken cancellationToken) =>
        Ok(await templates.ListAsync(query, cancellationToken));

    [HttpPost]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType<TemplateResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SaveTemplateRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await templates.CreateAsync(request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await templates.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
