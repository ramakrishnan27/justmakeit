using Platform.Auth.DTOs;
using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IPlatformService
{
    /// <summary>Every platform (channel), with whether it is enabled for the current organization.</summary>
    Task<IReadOnlyList<PlatformResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The uploaded icon of a platform. Throws NotFound for a platform with a built-in icon or that does not exist.</summary>
    Task<ChannelIconRecord> GetIconAsync(Guid channelId, CancellationToken cancellationToken);
}
