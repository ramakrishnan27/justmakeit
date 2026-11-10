using Microsoft.AspNetCore.Http;

namespace Platform.Auth.DTOs;

/// <summary>
/// The form a new channel is created from (<c>multipart/form-data</c>): a name, a description and, optionally, the icon as a
/// file. Everything is checked by the service; this only carries what the browser sent.
/// </summary>
public sealed class CreateChannelForm
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>The icon file. A part with no file, or an empty one, means no icon.</summary>
    public IFormFile? Icon { get; set; }
}
