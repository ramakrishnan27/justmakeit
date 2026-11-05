using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;

namespace Platform.Auth.Services;

/// <summary>
/// Both menus held in memory, so showing a menu reads no database except once per <see cref="CacheLifetime"/>.
/// A menu edit made here calls <see cref="Invalidate"/> and is seen at once; another instance learns of it
/// within the lifetime. The rules for a held snapshot are in <see cref="SnapshotCache{T}"/>.
/// </summary>
public sealed class MenuCache : IMenuCache
{
    /// <summary>Longer than the permission cache: a rename or a new order is not security-relevant, only a convenience.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private readonly SnapshotCache<MenuSnapshot> _snapshot;

    public MenuCache(IServiceScopeFactory scopes, TimeProvider time)
    {
        _snapshot = new SnapshotCache<MenuSnapshot>(CacheLifetime, time, async cancellationToken =>
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMenuRepository>().LoadSnapshotAsync(cancellationToken);
        });
    }

    public Task<MenuSnapshot> GetAsync(CancellationToken cancellationToken) => _snapshot.GetAsync(cancellationToken);

    public void Invalidate() => _snapshot.Invalidate();
}
