using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// The Site admin's channels: the list, creating one with an uploaded icon, and the icon itself. Every route needs the
/// <see cref="PolicyNames.SiteAdmin"/> policy and shares the session rate limit. Lists and answers are marked no-store; the
/// icon, which never changes once a channel exists, may be kept by the browser for an hour (it is still private).
/// </summary>
[ApiController]
[Route("v1/site/channels")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.SiteAdmin)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class SiteChannelsController(ISiteChannelService channels) : ControllerBase
{
    // A 100 KB icon plus the form around it. Bigger bodies are refused before they are read.
    private const int MaxRequestBytes = ChannelIconRules.MaxBytes + 20_000;

    /// <summary>Every channel, oldest first, with how many organizations have it.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<IReadOnlyList<SiteChannelResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SiteChannelResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await channels.ListAsync(cancellationToken));

    /// <summary>Creates a channel from a form: name, description and an optional PNG, JPEG or WebP icon of at most 100 KB.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<SiteChannelResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SiteChannelResponse>> Create([FromForm] CreateChannelForm form, CancellationToken cancellationToken)
    {
        var (image, contentType) = await ReadIconAsync(form.Icon, cancellationToken);
        var created = await channels.CreateAsync(form.Name, form.Description, image, contentType, cancellationToken);
        return Created($"/v1/site/channels/{created.Id}", created);
    }

    /// <summary>The uploaded icon of a channel, as the image that was stored. 404 for a channel that has a built-in icon or does not exist.</summary>
    [HttpGet("{id:guid}/icon")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Icon(Guid id, CancellationToken cancellationToken)
    {
        var icon = await channels.GetIconAsync(id, cancellationToken);

        // Served as the image it was checked to be, and never guessed at.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(icon.Image, icon.ContentType);
    }

    // Reads at most one byte more than the limit, so an oversize file is refused by the service without being read whole.
    private static async Task<(byte[]? Image, string? ContentType)> ReadIconAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return (null, null);
        }

        var buffer = new byte[Math.Min(file.Length, ChannelIconRules.MaxBytes + 1L)];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return (buffer, file.ContentType);
    }
}
