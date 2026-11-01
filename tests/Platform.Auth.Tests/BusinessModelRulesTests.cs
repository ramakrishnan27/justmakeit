using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class BusinessModelRulesTests
{
    // The table the business asked for, written out so a change to the rules shows up here.
    public static TheoryData<BusinessModel, OrganizationRole, bool> Pairs => new()
    {
        { BusinessModel.Company, OrganizationRole.OrganizationAdmin, true },
        { BusinessModel.Company, OrganizationRole.ClientManager, false },
        { BusinessModel.Company, OrganizationRole.MarketingManager, true },
        { BusinessModel.Company, OrganizationRole.ContentWriter, true },

        { BusinessModel.Agency, OrganizationRole.OrganizationAdmin, true },
        { BusinessModel.Agency, OrganizationRole.ClientManager, true },
        { BusinessModel.Agency, OrganizationRole.MarketingManager, true },
        { BusinessModel.Agency, OrganizationRole.ContentWriter, true },

        { BusinessModel.Creator, OrganizationRole.OrganizationAdmin, false },
        { BusinessModel.Creator, OrganizationRole.ClientManager, false },
        { BusinessModel.Creator, OrganizationRole.MarketingManager, false },
        { BusinessModel.Creator, OrganizationRole.ContentWriter, true }
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void IsAllowed_FollowsTheTableOfModelsAndRoles(BusinessModel model, OrganizationRole role, bool expected) =>
        Assert.Equal(expected, model.IsAllowed(role));

    [Fact]
    public void ThePairsTableCoversEveryModelAndRole()
    {
        var expected = Enum.GetValues<BusinessModel>().Length * Enum.GetValues<OrganizationRole>().Length;

        Assert.Equal(expected, Pairs.Count());
    }

    [Fact]
    public void AllowedRoles_ListsTheRolesOfEachModel()
    {
        Assert.Equal(
            [OrganizationRole.OrganizationAdmin, OrganizationRole.MarketingManager, OrganizationRole.ContentWriter],
            BusinessModel.Company.AllowedRoles());
        Assert.Equal(
            [OrganizationRole.OrganizationAdmin, OrganizationRole.ClientManager, OrganizationRole.MarketingManager, OrganizationRole.ContentWriter],
            BusinessModel.Agency.AllowedRoles());
        Assert.Equal([OrganizationRole.ContentWriter], BusinessModel.Creator.AllowedRoles());
    }

    [Theory]
    [InlineData(BusinessModel.Company, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Agency, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Creator, OrganizationRole.ContentWriter)]
    public void FounderRole_IsTheAdminOfACompanyOrAgency_AndTheContentWriterOfACreator(BusinessModel model, OrganizationRole expected) =>
        Assert.Equal(expected, model.FounderRole());

    [Fact]
    public void TheFounderRole_IsAlwaysARoleTheModelAllows()
    {
        foreach (var model in Enum.GetValues<BusinessModel>())
        {
            Assert.True(model.IsAllowed(model.FounderRole()), $"{model} does not allow its own founder role.");
        }
    }

    [Theory]
    [InlineData(BusinessModel.Company, true)]
    [InlineData(BusinessModel.Agency, true)]
    [InlineData(BusinessModel.Creator, false)]
    public void AllowsSeveralWorkspaces_IsFalseOnlyForACreator(BusinessModel model, bool expected) =>
        Assert.Equal(expected, model.AllowsSeveralWorkspaces());

    [Theory]
    [InlineData("company", BusinessModel.Company)]
    [InlineData("agency", BusinessModel.Agency)]
    [InlineData("creator", BusinessModel.Creator)]
    public void TryParse_ReadsTheStoredLowerCaseName(string value, BusinessModel expected)
    {
        Assert.True(BusinessModelRules.TryParse(value, out var model));
        Assert.Equal(expected, model);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("AGENCY")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData(" company")]
    [InlineData("creator ")]
    [InlineData("individual")]
    [InlineData("individual creator")]
    [InlineData("enterprise")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsAnythingButTheExactName(string? value)
    {
        Assert.False(BusinessModelRules.TryParse(value, out _));
    }

    [Fact]
    public void AnUnknownModel_IsRefusedRatherThanGivenRoles()
    {
        var unknown = (BusinessModel)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.AllowedRoles());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.FounderRole());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.IsAllowed(OrganizationRole.OrganizationAdmin));
    }
}
