using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData(AgencyRole.Owner, true)]
    [InlineData(AgencyRole.Admin, true)]
    [InlineData(AgencyRole.Manager, true)]
    [InlineData(AgencyRole.Viewer, false)]
    public void CanWrite_ExcludesOnlyViewers(AgencyRole role, bool expected) =>
        Assert.Equal(expected, role.CanWrite());

    [Theory]
    [InlineData(AgencyRole.Owner, true)]
    [InlineData(AgencyRole.Admin, true)]
    [InlineData(AgencyRole.Manager, false)]
    [InlineData(AgencyRole.Viewer, false)]
    public void CanManage_IsForOwnersAndAdminsOnly(AgencyRole role, bool expected) =>
        Assert.Equal(expected, role.CanManage());

    [Theory]
    [InlineData(AgencyRole.Owner)]
    [InlineData(AgencyRole.Admin)]
    [InlineData(AgencyRole.Manager)]
    [InlineData(AgencyRole.Viewer)]
    public void CanRead_IsTrueForEveryRole(AgencyRole role) => Assert.True(role.CanRead());

    [Theory]
    [InlineData("owner", AgencyRole.Owner)]
    [InlineData("admin", AgencyRole.Admin)]
    [InlineData("manager", AgencyRole.Manager)]
    [InlineData("viewer", AgencyRole.Viewer)]
    public void TryParse_ReadsTheStoredLowerCaseName(string value, AgencyRole expected)
    {
        Assert.True(RolePermissions.TryParse(value, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("ADMIN")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" owner")]
    [InlineData("owner ")]
    [InlineData("superuser")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingButTheExactName(string? value)
    {
        Assert.False(RolePermissions.TryParse(value, out _));
    }

    [Fact]
    public void NamesWhere_ListsTheStoredNamesOfTheAllowedRoles()
    {
        Assert.Equal(["owner", "admin"], RolePermissions.NamesWhere(RolePermissions.CanManage));
        Assert.Equal(["owner", "admin", "manager"], RolePermissions.NamesWhere(RolePermissions.CanWrite));
        Assert.Equal(["owner", "admin", "manager", "viewer"], RolePermissions.NamesWhere(RolePermissions.CanRead));
    }
}
