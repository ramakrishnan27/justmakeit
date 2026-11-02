using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Services;

/// <summary>
/// Holds the permission matrix and its version in memory (one consistent snapshot, a few dozen rows), so
/// issuing a token and checking one cost no database read except once per <see cref="CacheLifetime"/>.
/// <para>
/// A change the Site admin makes calls <see cref="Invalidate"/> after it commits: this process sees it at
/// once, and tokens issued under the old version are refused. Other instances catch up within the
/// lifetime. The rules for a held snapshot are in <see cref="SnapshotCache{T}"/>.
/// </para>
/// </summary>
public sealed class PermissionService : IPermissionService
{
    /// <summary>How long a snapshot is served before it is read again. The bound on how late another instance learns of a change.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly SnapshotCache<PermissionSnapshot> _snapshot;

    public PermissionService(IServiceScopeFactory scopes, TimeProvider time)
    {
        // The repository is scoped, so every load opens a scope of its own.
        _snapshot = new SnapshotCache<PermissionSnapshot>(CacheLifetime, time, async cancellationToken =>
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IPermissionRepository>().LoadSnapshotAsync(cancellationToken);
        });
    }

    public async Task<PermissionGrants> GetGrantsAsync(
        BusinessModel businessModel, OrganizationRole role, CancellationToken cancellationToken)
    {
        var snapshot = await _snapshot.GetAsync(cancellationToken);
        IReadOnlyList<string> keys = snapshot.Granted.TryGetValue((businessModel, role), out var granted) ? granted : Array.Empty<string>();
        return new PermissionGrants(snapshot.Version, keys);
    }

    public async Task<long> GetVersionAsync(CancellationToken cancellationToken) =>
        (await _snapshot.GetAsync(cancellationToken)).Version;

    public void Invalidate() => _snapshot.Invalidate();
}
