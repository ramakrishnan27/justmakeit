using Platform.Auth.Interfaces;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// A session revocation service for hosts that have no database: no session has ever been ended. It lets a test decide
/// authorization from the token alone. <see cref="ApiHostFactory"/> installs it whenever the host's database address is
/// the unreachable one, so no test can reach a database by accident.
/// </summary>
public sealed class DefaultSessionRevocationService : ISessionRevocationService
{
    public Task<bool> IsRevokedAsync(Guid agencyId, Guid userId, long issuedAtSeconds, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public void Invalidate()
    {
    }
}
