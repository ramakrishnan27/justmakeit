using Platform.Auth.DTOs;
using Platform.Auth.Services;

namespace Platform.Auth.Tests;

public sealed class MenuRequestsTests
{
    private static string[] FieldsOf(Platform.Helpers.Validation.ValidationErrors errors) => [.. errors.ToDictionary().Keys.Order()];

    // --- Labels -------------------------------------------------------------------------------

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("  Padded  ")]
    [InlineData("Équipes & clients")]
    public void ALabel_OfOrdinaryWords_IsAccepted(string label) =>
        Assert.False(new CreateMenuItemRequest("workspaces", label, "home", true).Validate().Any);

    [Fact]
    public void ALabel_OfExactlySixtyCharacters_IsAccepted_AndSixtyOneIsNot()
    {
        Assert.False(new UpdateMenuItemRequest(new string('a', 60), "home", true).Validate().Any);
        Assert.Contains("label", FieldsOf(new UpdateMenuItemRequest(new string('a', 61), "home", true).Validate()));
    }

    [Fact]
    public void ALabelIsMeasuredAfterTrimming_SoPaddingDoesNotUseUpTheLimit() =>
        Assert.False(new UpdateMenuItemRequest("  " + new string('a', 60) + "  ", "home", true).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Two\nlines")]
    [InlineData("Tab\there")]
    [InlineData("Bell\a")]
    [InlineData("Null\0byte")]
    public void ALabel_ThatIsEmptyOrHoldsAControlCharacter_IsRefused(string? label) =>
        Assert.Equal(["label"], FieldsOf(new UpdateMenuItemRequest(label, "home", true).Validate()));

    // --- Icons --------------------------------------------------------------------------------

    [Fact]
    public void EveryIconInTheList_IsAccepted()
    {
        foreach (var icon in MenuIcons.All)
        {
            Assert.False(new UpdateMenuItemRequest("Label", icon, true).Validate().Any, icon);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Home")]
    [InlineData("rocket")]
    [InlineData("home ")]
    [InlineData("../home")]
    [InlineData("<script>")]
    public void AnIcon_ThatIsNotInTheList_IsRefused(string? icon) =>
        Assert.Equal(["icon"], FieldsOf(new UpdateMenuItemRequest("Label", icon, true).Validate()));

    // --- Page keys ----------------------------------------------------------------------------

    [Theory]
    [InlineData("workspaces")]
    [InlineData("a")]
    [InlineData("email-lists")]
    [InlineData("page2")]
    public void APageKey_OfLowerCaseWordsAndHyphens_IsAccepted(string key) =>
        Assert.False(new CreateMenuItemRequest(key, "Label", "home", null).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Workspaces")]
    [InlineData("1page")]
    [InlineData("-page")]
    [InlineData("two words")]
    [InlineData("page/../x")]
    [InlineData("page_key")]
    [InlineData("é")]
    public void APageKey_OfAnyOtherShape_IsRefused(string? key) =>
        Assert.Equal(["pageKey"], FieldsOf(new CreateMenuItemRequest(key, "Label", "home", null).Validate()));

    [Fact]
    public void APageKey_OfFortyCharactersIsAccepted_AndFortyOneIsNot()
    {
        Assert.False(new CreateMenuItemRequest(new string('a', 40), "Label", "home", null).Validate().Any);
        Assert.Equal(["pageKey"], FieldsOf(new CreateMenuItemRequest(new string('a', 41), "Label", "home", null).Validate()));
    }

    // --- Create and update --------------------------------------------------------------------

    [Fact]
    public void Create_DoesNotNeedAVisibility_ItDefaultsToVisible() =>
        Assert.False(new CreateMenuItemRequest("workspaces", "Label", "home", null).Validate().Any);

    [Fact]
    public void Create_ReportsEveryBadFieldAtOnce() =>
        Assert.Equal(["icon", "label", "pageKey"], FieldsOf(new CreateMenuItemRequest(null, null, null, null).Validate()));

    [Fact]
    public void Update_NeedsAVisibility_ItIsSentEveryTime() =>
        Assert.Equal(["isVisible"], FieldsOf(new UpdateMenuItemRequest("Label", "home", null).Validate()));

    // --- Reorder ------------------------------------------------------------------------------

    [Fact]
    public void Reorder_OfADistinctList_IsAccepted() =>
        Assert.False(new ReorderMenuRequest([Guid.NewGuid(), Guid.NewGuid()]).Validate().Any);

    [Fact]
    public void Reorder_OfAnEmptyList_IsAccepted_ItIsTheDatabaseThatKnowsWhatTheMenuHolds() =>
        Assert.False(new ReorderMenuRequest([]).Validate().Any);

    [Fact]
    public void Reorder_WithoutAList_IsRefused() =>
        Assert.Equal(["itemIds"], FieldsOf(new ReorderMenuRequest(null).Validate()));

    [Fact]
    public void Reorder_WithAnEmptyId_IsRefused() =>
        Assert.Equal(["itemIds"], FieldsOf(new ReorderMenuRequest([Guid.NewGuid(), Guid.Empty]).Validate()));

    [Fact]
    public void Reorder_WithAnIdTwice_IsRefused()
    {
        var id = Guid.NewGuid();

        Assert.Equal(["itemIds"], FieldsOf(new ReorderMenuRequest([id, Guid.NewGuid(), id]).Validate()));
    }

    [Fact]
    public void Reorder_OfMoreThanTheLimit_IsRefused()
    {
        var tooMany = Enumerable.Range(0, ReorderMenuRequest.MaxItems + 1).Select(_ => Guid.NewGuid()).ToList();

        Assert.Equal(["itemIds"], FieldsOf(new ReorderMenuRequest(tooMany).Validate()));
        Assert.False(new ReorderMenuRequest(tooMany.Take(ReorderMenuRequest.MaxItems).ToList()).Validate().Any);
    }

    // --- Grants -------------------------------------------------------------------------------

    [Fact]
    public void ASetGrantRequest_NamingAModelARoleAndAValue_IsAccepted() =>
        Assert.False(new SetGrantRequest("agency", "clientmanager", false).Validate().Any);

    [Fact]
    public void ASetGrantRequest_ReportsEveryBadFieldAtOnce() =>
        Assert.Equal(["businessModel", "granted", "role"], FieldsOf(new SetGrantRequest(null, null, null).Validate()));

    [Theory]
    [InlineData("Company")]
    [InlineData("0")]
    [InlineData("holding")]
    public void ABusinessModel_ThatIsNotTheExactStoredName_IsRefused(string model) =>
        Assert.Equal(["businessModel"], FieldsOf(new SetGrantRequest(model, "organizationadmin", true).Validate()));

    [Theory]
    [InlineData("OrganizationAdmin")]
    [InlineData("owner")]
    [InlineData("siteadmin")]
    [InlineData("1")]
    public void ARole_ThatIsNotTheExactStoredName_IsRefused(string role) =>
        Assert.Equal(["role"], FieldsOf(new SetGrantRequest("company", role, true).Validate()));
}
