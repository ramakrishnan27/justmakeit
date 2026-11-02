using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// A permission service for hosts that have no database: every role holds what it holds in a fresh
/// installation (<see cref="RolePermissions.GrantedByDefault"/>) and the permission version never moves.
/// It lets a test decide authorization from the token alone. <see cref="ApiHostFactory"/> installs it
/// whenever the host's database address is the unreachable one, so no test can reach a database by accident.
/// </summary>
public sealed class DefaultGrantsPermissionService : IPermissionService
{
    public const long Version = 1;

    public Task<PermissionGrants> GetGrantsAsync(BusinessModel businessModel, OrganizationRole role, CancellationToken cancellationToken) =>
        Task.FromResult(new PermissionGrants(Version, DefaultKeys(role)));

    public Task<long> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult(Version);

    public void Invalidate()
    {
    }

    /// <summary>The keys the role holds in a fresh installation.</summary>
    public static string[] DefaultKeys(OrganizationRole role) =>
        PermissionKeys.All.Where(key => role.GrantedByDefault(key)).ToArray();
}
