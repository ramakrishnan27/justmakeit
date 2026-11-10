using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

/// <summary>
/// The channels and which organization has which, as the Site admin manages them. Every call is a Site-admin-only database
/// function; each throws <see cref="Platform.Helpers.Exceptions.ForbiddenException"/> (<c>not_site_admin</c>) when the
/// database refuses because the caller is not one.
/// </summary>
public interface ISiteChannelRepository
{
    Task<IReadOnlyList<SiteChannelRecord>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a channel. Throws a conflict (<c>channel_exists</c>) when the name is taken, whatever the letter case.</summary>
    Task<SiteChannelRecord> CreateAsync(Guid userId, NewChannel channel, CancellationToken cancellationToken);

    /// <summary>The uploaded image of a channel, or null when it has a built-in icon or does not exist.</summary>
    Task<ChannelIconRecord?> GetIconAsync(Guid userId, Guid channelId, CancellationToken cancellationToken);

    /// <summary>Every channel with whether it is enabled for the organization. Throws not found (<c>organization_not_found</c>) for an unknown one.</summary>
    Task<IReadOnlyList<OrganizationChannelRecord>> ListForOrganizationAsync(Guid userId, Guid agencyId, CancellationToken cancellationToken);

    /// <summary>Enables or disables a channel for an organization. True when something changed; repeating a call changes nothing.</summary>
    Task<bool> SetForOrganizationAsync(Guid userId, Guid agencyId, Guid channelId, bool enabled, CancellationToken cancellationToken);
}
