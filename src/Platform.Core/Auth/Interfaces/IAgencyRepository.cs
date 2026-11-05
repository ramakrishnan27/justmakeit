using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IAgencyRepository
{
    /// <summary>The agency, as seen by a member of it. Null when the user cannot see it.</summary>
    Task<AgencyRecord?> GetAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);
}
