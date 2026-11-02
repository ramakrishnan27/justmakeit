using Dapper;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="PermissionRepository"/> over a real database, connecting as app_user with no tenant and
/// no user, exactly as the permission cache does while a person is still signing in.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class PermissionRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private PermissionRepository Repository() => new(_db.CreateFactory());

    [Fact]
    public async Task Load_ReturnsEverySlotsDefaultGrants_AndTheVersion_WithoutAnyUser()
    {
        var snapshot = await Repository().LoadSnapshotAsync(CancellationToken.None);

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state"), snapshot.Version);

        var slots = Enum.GetValues<BusinessModel>()
            .SelectMany(model => model.AllowedRoles().Select(role => (model, role)))
            .ToList();
        Assert.Equal(slots.Count, snapshot.Granted.Count);
        foreach (var (model, role) in slots)
        {
            var expected = PermissionKeys.All.Where(key => role.GrantedByDefault(key)).Order(StringComparer.Ordinal);
            Assert.Equal(expected, snapshot.Granted[(model, role)]);
        }
    }

    [Fact]
    public async Task Load_ReflectsARevoke_AndTheVersionItMoved()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var before = await Repository().LoadSnapshotAsync(CancellationToken.None);

        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'agency' AND role = 'organizationadmin' AND permission_key = 'workspaces.create'");
        var after = await Repository().LoadSnapshotAsync(CancellationToken.None);

        Assert.Equal(before.Version + 1, after.Version);
        Assert.Contains(PermissionKeys.WorkspacesCreate, before.Granted[(BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]);
        Assert.DoesNotContain(PermissionKeys.WorkspacesCreate, after.Granted[(BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]);
        // Only that slot changed.
        Assert.Contains(PermissionKeys.WorkspacesCreate, after.Granted[(BusinessModel.Company, OrganizationRole.OrganizationAdmin)]);
    }

    [Fact]
    public async Task Load_WithNothingGranted_StillReturnsTheVersion_AndNoSlots()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("DELETE FROM role_permissions");

        var snapshot = await Repository().LoadSnapshotAsync(CancellationToken.None);

        Assert.Empty(snapshot.Granted);
        Assert.Equal(await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state"), snapshot.Version);
    }

    [Fact]
    public async Task Load_WithARevokedSlot_OmitsTheKeyButKeepsTheSlot()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key <> 'dashboard.view'");

        var snapshot = await Repository().LoadSnapshotAsync(CancellationToken.None);

        Assert.Equal([PermissionKeys.DashboardView], snapshot.Granted[(BusinessModel.Company, OrganizationRole.MarketingManager)]);
    }

    [Fact]
    public async Task Load_WithoutTheVersionRow_Throws_SoNoTokenIsIssuedOrCheckedAgainstNothing()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("DELETE FROM permission_state");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository().LoadSnapshotAsync(CancellationToken.None));
    }
}
