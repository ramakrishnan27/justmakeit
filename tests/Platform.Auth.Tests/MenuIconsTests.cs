using System.Text.RegularExpressions;
using Platform.Auth.Services;

namespace Platform.Auth.Tests;

public sealed partial class MenuIconsTests
{
    // The same shape the menu_items.icon CHECK in 005_menus_permissions.sql accepts.
    [GeneratedRegex("^[a-z][a-z0-9-]{0,29}$")]
    private static partial Regex IconShape();

    [Fact]
    public void EveryIcon_IsUnique_AndHasTheShapeTheDatabaseAccepts()
    {
        Assert.Equal(MenuIcons.All.Count, MenuIcons.All.Distinct().Count());
        Assert.All(MenuIcons.All, icon => Assert.Matches(IconShape(), icon));
    }

    [Fact]
    public void ThereAreTenIcons_AndTheCircleIsAmongThem_BecauseItIsWhatAnUnknownNameFallsBackTo()
    {
        Assert.Equal(10, MenuIcons.All.Count);
        Assert.Contains("circle", MenuIcons.All);
    }

    [Theory]
    [InlineData("home", true)]
    [InlineData("Home", false)]
    [InlineData("home ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_IsAnExactMatch(string? icon, bool expected) => Assert.Equal(expected, MenuIcons.IsKnown(icon));

    [Theory]
    [InlineData("member", true)]
    [InlineData("admin", true)]
    [InlineData("Member", false)]
    [InlineData("members", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AMenuKey_IsOneOfTheTwoMenus(string? key, bool expected) => Assert.Equal(expected, MenuKeys.IsKnown(key));
}
