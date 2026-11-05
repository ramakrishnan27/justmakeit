using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IMembershipRepository
{
    /// <summary>Every agency the user belongs to, most recently used first.</summary>
    Task<IReadOnlyList<MembershipRecord>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<MembershipRecord?> FindAsync(Guid userId, Guid agencyId, CancellationToken cancellationToken);

    /// <summary>Records that the user just used this agency, so the next login without a choice picks it.</summary>
    Task TouchAsync(Guid agencyId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
