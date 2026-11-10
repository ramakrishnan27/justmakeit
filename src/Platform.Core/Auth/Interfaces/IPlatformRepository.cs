using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

/// <summary>
/// The platforms (channels) as a member sees them. The tables are closed to the application role, so every call goes through a
/// function of <c>012_member_platforms.sql</c> that decides from the connection's user and organization whether it may happen.
/// </summary>
public interface IPlatformRepository
{
    /// <summary>Every channel, oldest first, with whether it is enabled for the organization.</summary>
    Task<IReadOnlyList<OrganizationChannelRecord>> ListAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The uploaded icon of a channel, or null when it has a built-in icon or does not exist.</summary>
    Task<ChannelIconRecord?> GetIconAsync(Guid agencyId, Guid userId, Guid channelId, CancellationToken cancellationToken);
}
