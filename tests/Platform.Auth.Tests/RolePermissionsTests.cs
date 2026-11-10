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

    // The factory defaults: 005_menus_permissions.sql seeds exactly this matrix and an integration test
    // compares the two, so today's behaviour does not change when the policies start reading the tables.
    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.DashboardView, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.DashboardView, true)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.DashboardView, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.DashboardView, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.WorkspacesView, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.WorkspacesView, true)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.WorkspacesView, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.WorkspacesView, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.WorkspacesCreate, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.WorkspacesCreate, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.WorkspacesCreate, false)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.WorkspacesCreate, false)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.DataRead, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.DataRead, true)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.DataRead, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.DataRead, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.DataWrite, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.DataWrite, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.DataWrite, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.DataWrite, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.OrganizationManage, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.OrganizationManage, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.OrganizationManage, false)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.OrganizationManage, false)]
    public void GrantedByDefault_IsTheMatrixThatTheDatabaseIsSeededWith(OrganizationRole role, string key, bool expected) =>
        Assert.Equal(expected, role.GrantedByDefault(key));

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.ContentStudioCreate, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.ContentStudioCreate, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.ContentStudioCreate, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.ContentStudioCreate, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.ContentStudioPublish, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.ContentStudioPublish, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.ContentStudioPublish, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.ContentStudioPublish, false)]
    public void GrantedByDefault_OfTheContentStudioKeys_ForARoleInEveryModelItIsIn(OrganizationRole role, string key, bool expected) =>
        Assert.Equal(expected, role.GrantedByDefault(key));

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.MediaLibraryCreate, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.MediaLibraryCreate, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.MediaLibraryCreate, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.MediaLibraryCreate, true)]
    [InlineData(OrganizationRole.OrganizationAdmin, PermissionKeys.MediaLibraryDelete, true)]
    [InlineData(OrganizationRole.ClientManager, PermissionKeys.MediaLibraryDelete, false)]
    [InlineData(OrganizationRole.MarketingManager, PermissionKeys.MediaLibraryDelete, true)]
    [InlineData(OrganizationRole.ContentWriter, PermissionKeys.MediaLibraryDelete, false)]
    public void GrantedByDefault_OfTheMediaLibraryKeys_ForARoleInEveryModelItIsIn(OrganizationRole role, string key, bool expected) =>
        Assert.Equal(expected, role.GrantedByDefault(key));

    [Theory]
    [InlineData(BusinessModel.Company, false)]
    [InlineData(BusinessModel.Agency, false)]
    [InlineData(BusinessModel.Creator, true)]
    public void AContentWriterPublishesOnlyInAnIndividualCreatorsOrganization(BusinessModel model, bool expected) =>
        Assert.Equal(expected, OrganizationRole.ContentWriter.GrantedByDefault(model, PermissionKeys.ContentStudioPublish));

    [Theory]
    [InlineData(BusinessModel.Company, false)]
    [InlineData(BusinessModel.Agency, false)]
    [InlineData(BusinessModel.Creator, true)]
    public void AContentWriterDeletesMediaOnlyInAnIndividualCreatorsOrganization(BusinessModel model, bool expected) =>
        Assert.Equal(expected, OrganizationRole.ContentWriter.GrantedByDefault(model, PermissionKeys.MediaLibraryDelete));

    [Fact]
    public void TheModelAwareDefault_DiffersFromTheRoleOnlyOneInExactlyTheCreatorsContentWritersSlots()
    {
        var differences = new List<string>();
        foreach (var model in Enum.GetValues<BusinessModel>())
        {
            foreach (var role in model.AllowedRoles())
            {
                foreach (var key in PermissionKeys.All)
                {
                    if (role.GrantedByDefault(model, key) != role.GrantedByDefault(key))
                    {
                        differences.Add($"{model}/{role} {key}");
                    }
                }
            }
        }

        // Where a creator works alone, the content writer also publishes and also takes files out of the Media Library.
        Assert.Equal(["Creator/ContentWriter contentstudio.publish", "Creator/ContentWriter medialibrary.delete"], differences);
    }

    [Fact]
    public void GrantedByDefault_HasAnAnswerForEveryRoleAndEveryKey_SoANewKeyCannotBeForgotten()
    {
        foreach (var role in Enum.GetValues<OrganizationRole>())
        {
            foreach (var key in PermissionKeys.All)
            {
                var ex = Record.Exception(() => role.GrantedByDefault(key));
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public void GrantedByDefault_GivesAProtectedPermissionToEveryRole()
    {
        foreach (var key in PermissionKeys.All.Where(PermissionKeys.IsProtected))
        {
            Assert.All(Enum.GetValues<OrganizationRole>(), role => Assert.True(role.GrantedByDefault(key)));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("workspaces")]
    [InlineData("Dashboard.View")]
    [InlineData("content.write")]
    public void GrantedByDefault_RefusesAKeyThatIsNotInTheCatalog(string key) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OrganizationRole.OrganizationAdmin.GrantedByDefault(key));
}
