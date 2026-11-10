using Npgsql;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class NpgsqlConnectionFactoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task OpenAsync_WithExplicitTenant_SetsBothValuesForThePolicies()
    {
        var agency = Guid.NewGuid();
        var user = Guid.NewGuid();

        await using var connection = await _db.CreateFactory().OpenAsync(agency, user, CancellationToken.None);

        Assert.Equal(agency, await DbTestSupport.ScalarAsync<Guid>(connection, "SELECT app_agency_id()"));
        Assert.Equal(user, await DbTestSupport.ScalarAsync<Guid>(connection, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task OpenAsync_UsesTheCurrentTenantContext()
    {
        var agency = Guid.NewGuid();
        var user = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.Set(agency, user, "organizationadmin");
        await using var dataSource = NpgsqlDataSource.Create(_db.AppConnectionString);
        var factory = new NpgsqlConnectionFactory(dataSource, tenant);

        await using var connection = await factory.OpenAsync(CancellationToken.None);

        Assert.Equal(agency, await DbTestSupport.ScalarAsync<Guid>(connection, "SELECT app_agency_id()"));
        Assert.Equal(user, await DbTestSupport.ScalarAsync<Guid>(connection, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task OpenAsync_FollowsTheContextAsItChangesBetweenCalls()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var tenant = new TenantContext();
        await using var dataSource = NpgsqlDataSource.Create(_db.AppConnectionString);
        var factory = new NpgsqlConnectionFactory(dataSource, tenant);

        tenant.Set(first, null);
        await using (var one = await factory.OpenAsync(CancellationToken.None))
        {
            Assert.Equal(first, await DbTestSupport.ScalarAsync<Guid>(one, "SELECT app_agency_id()"));
        }

        tenant.Set(second, null);
        await using var two = await factory.OpenAsync(CancellationToken.None);
        Assert.Equal(second, await DbTestSupport.ScalarAsync<Guid>(two, "SELECT app_agency_id()"));
    }

    [Fact]
    public async Task OpenAsync_WithNoTenant_LeavesBothValuesNull()
    {
        await using var connection = await _db.CreateFactory().OpenAsync(null, null, CancellationToken.None);

        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(connection, "SELECT app_agency_id()"));
        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(connection, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task OpenAsync_ReusingAPooledConnection_NeverLeaksThePreviousTenant()
    {
        // One physical connection in the pool, so the second open is guaranteed to reuse the first.
        var pooled = new NpgsqlConnectionStringBuilder(_db.AppConnectionString)
        {
            Pooling = true,
            MaxPoolSize = 1,
            MinPoolSize = 0
        };
        var factory = _db.CreateFactory(connectionString: pooled.ConnectionString);

        int firstBackendPid;
        await using (var first = await factory.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None))
        {
            firstBackendPid = first.ProcessID;
        }

        await using var second = await factory.OpenAsync(null, null, CancellationToken.None);

        Assert.Equal(firstBackendPid, second.ProcessID); // really the same session
        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(second, "SELECT app_agency_id()"));
        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(second, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task OpenAsync_Values_SurviveTransactionsOnTheSameConnection()
    {
        var agency = Guid.NewGuid();
        await using var connection = await _db.CreateFactory().OpenAsync(agency, null, CancellationToken.None);

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await transaction.CommitAsync();
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await transaction.RollbackAsync();
        }

        Assert.Equal(agency, await DbTestSupport.ScalarAsync<Guid>(connection, "SELECT app_agency_id()"));
    }

    [Fact]
    public async Task OpenAsync_WithABadPassword_Throws()
    {
        var bad = new NpgsqlConnectionStringBuilder(_db.AppConnectionString) { Password = "definitely-wrong" };
        var factory = _db.CreateFactory(connectionString: bad.ConnectionString);

        await Assert.ThrowsAsync<PostgresException>(() => factory.OpenAsync(null, null, CancellationToken.None));
    }
}
