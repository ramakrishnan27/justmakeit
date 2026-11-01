using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class RolePermissionsTests
{
    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin, true)]
    [InlineData(OrganizationRole.ClientManager, false)]
    [InlineData(OrganizationRole.MarketingManager, true)]
    [InlineData(OrganizationRole.ContentWriter, true)]
    public void CanWrite_ExcludesOnlyTheClientManager(OrganizationRole role, bool expected) =>
        Assert.Equal(expected, role.CanWrite());

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin, true)]
    [InlineData(OrganizationRole.ClientManager, false)]
    [InlineData(OrganizationRole.MarketingManager, false)]
    [InlineData(OrganizationRole.ContentWriter, false)]
    public void CanManage_IsForOrganizationAdminsOnly(OrganizationRole role, bool expected) =>
        Assert.Equal(expected, role.CanManage());

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public void CanRead_IsTrueForEveryRole(OrganizationRole role) => Assert.True(role.CanRead());

    [Fact]
    public void ThereAreExactlyFourRoles_SoAddingOneMeansDecidingWhatItMayDo()
    {
        // The theories above list every role. A fifth one must be added to them, to the policies and
        // to the CHECK in 003_business_models.sql on purpose.
        Assert.Equal(4, Enum.GetValues<OrganizationRole>().Length);
    }

    [Fact]
    public void CanRead_IsFalseForAValueThatIsNotARole() => Assert.False(((OrganizationRole)99).CanRead());

    [Theory]
    [InlineData("organizationadmin", OrganizationRole.OrganizationAdmin)]
    [InlineData("clientmanager", OrganizationRole.ClientManager)]
    [InlineData("marketingmanager", OrganizationRole.MarketingManager)]
    [InlineData("contentwriter", OrganizationRole.ContentWriter)]
    public void TryParse_ReadsTheStoredLowerCaseName(string value, OrganizationRole expected)
    {
        Assert.True(RolePermissions.TryParse(value, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("OrganizationAdmin")]
    [InlineData("organizationAdmin")]
    [InlineData("ORGANIZATIONADMIN")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" organizationadmin")]
    [InlineData("organizationadmin ")]
    [InlineData("organization admin")]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("viewer")]
    [InlineData("siteadmin")]
    [InlineData("superuser")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingButTheExactName_AndTheRolesThatNoLongerExist(string? value)
    {
        Assert.False(RolePermissions.TryParse(value, out _));
    }

    [Fact]
    public void NamesWhere_ListsTheStoredNamesOfTheAllowedRoles()
    {
        Assert.Equal(["organizationadmin"], RolePermissions.NamesWhere(RolePermissions.CanManage));
        Assert.Equal(
            ["organizationadmin", "marketingmanager", "contentwriter"], RolePermissions.NamesWhere(RolePermissions.CanWrite));
        Assert.Equal(
            ["organizationadmin", "clientmanager", "marketingmanager", "contentwriter"],
            RolePermissions.NamesWhere(RolePermissions.CanRead));
    }
}
