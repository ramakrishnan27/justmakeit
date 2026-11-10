using Platform.Auth.DTOs;
using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface ISiteChannelService
{
    Task<IReadOnlyList<SiteChannelResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a channel. <paramref name="image"/> is the uploaded icon (null for none: the channel gets the default glyph) and
    /// <paramref name="declaredContentType"/> what the browser said it is.
    /// </summary>
    Task<SiteChannelResponse> CreateAsync(
        string? name, string? description, byte[]? image, string? declaredContentType, CancellationToken cancellationToken);

    /// <summary>The uploaded image of a channel. Not found when the channel has a built-in icon or does not exist.</summary>
    Task<ChannelIconRecord> GetIconAsync(Guid channelId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationChannelResponse>> ListForOrganizationAsync(Guid agencyId, CancellationToken cancellationToken);

    Task SetForOrganizationAsync(Guid agencyId, Guid channelId, SetOrganizationChannelRequest request, CancellationToken cancellationToken);
}

public interface ISiteOrganizationService
{
    /// <summary>Deactivates or activates an organization. Deactivating signs every member out at once.</summary>
    Task SetActiveAsync(Guid agencyId, SetOrganizationActiveRequest request, CancellationToken cancellationToken);
}
