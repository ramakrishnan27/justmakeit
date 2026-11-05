using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;

namespace Platform.Auth.Services;

/// <summary>
/// Holds the sessions that were ended by changing or removing a member (a few rows, only recent ones) in memory, so a
/// request costs no database read except once per <see cref="CacheLifetime"/>. The team service calls
/// <see cref="Invalidate"/> after every change, so this process refuses the old token at once; other instances learn
/// of it within the lifetime. The rules for a held snapshot are in <see cref="SnapshotCache{T}"/>.
/// </summary>
public sealed class SessionRevocationService : ISessionRevocationService
{
    /// <summary>How long a snapshot is served before it is read again. The bound on how late another instance refuses a token.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly SnapshotCache<SessionRevocationSnapshot> _snapshot;

    public SessionRevocationService(IServiceScopeFactory scopes, TimeProvider time)
    {
        // The repository is scoped, so every load opens a scope of its own.
        _snapshot = new SnapshotCache<SessionRevocationSnapshot>(CacheLifetime, time, async cancellationToken =>
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISessionRevocationRepository>().LoadSnapshotAsync(cancellationToken);
        });
    }

    public async Task<bool> IsRevokedAsync(Guid agencyId, Guid userId, long issuedAtSeconds, CancellationToken cancellationToken) =>
        (await _snapshot.GetAsync(cancellationToken)).IsRevoked(agencyId, userId, issuedAtSeconds);

    public void Invalidate() => _snapshot.Invalidate();
}
