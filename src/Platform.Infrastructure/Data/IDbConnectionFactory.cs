using Npgsql;

namespace Platform.Infrastructure.Data;

public interface IDbConnectionFactory
{
    /// <summary>Opens a connection scoped to the current <see cref="Helpers.Tenancy.ITenantContext"/>.</summary>
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens a connection scoped to an explicit tenant. Background workers use it for the agency they
    /// are processing; registration and pre-login lookups use it before a tenant context exists.
    /// </summary>
    Task<NpgsqlConnection> OpenAsync(Guid? agencyId, Guid? userId, CancellationToken cancellationToken);
}
