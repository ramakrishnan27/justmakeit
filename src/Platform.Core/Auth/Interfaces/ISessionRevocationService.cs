using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface ISessionRevocationRepository
{
    /// <summary>Every recent ended session. Needs no user: it is read for the API itself, not on behalf of a person.</summary>
    Task<SessionRevocationSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The sessions that were ended by changing or removing a member, held in memory so no request reads the database for
/// them. A token has no other way to be taken back, and it lasts up to 30 days.
/// </summary>
public interface ISessionRevocationService
{
    /// <summary>Whether an access token issued at <paramref name="issuedAtSeconds"/> (seconds since 1970) for this person is void.</summary>
    Task<bool> IsRevokedAsync(Guid agencyId, Guid userId, long issuedAtSeconds, CancellationToken cancellationToken);

    /// <summary>Forgets what is held, so the next call reads the database. Call it after a change was made.</summary>
    void Invalidate();
}
