using Platform.Content.Data;
using Platform.Content.Enums;
using Platform.Content.Features.Hashtags;
using Platform.Content.Features.Publishing;
using Platform.Helpers.Validation;

namespace Platform.Content.Tests;

public sealed class HashtagAndRulesTests
{
    [Theory]
    [InlineData("GrowTogether", "GrowTogether")]
    [InlineData("#GrowTogether", "GrowTogether")]
    [InlineData("  ##Grow  ", "Grow")]
    [InlineData("# spaced", "spaced")]
    [InlineData("नमस्ते", "नमस्ते")]
    public void TryNormalize_StripsTheLeadingHashAndTheSpacesAroundIt(string raw, string expected)
    {
        Assert.True(HashtagNormalizer.TryNormalize(raw, out var hashtag));
        Assert.Equal(expected, hashtag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("two words")]
    [InlineData("tab\there")]
    [InlineData("in#side")]
    [InlineData("line\nbreak")]
    [InlineData(null)]
    public void TryNormalize_RefusesWhatIsNotAHashtag(string? raw) => Assert.False(HashtagNormalizer.TryNormalize(raw, out _));

    [Fact]
    public void TryNormalize_RefusesOverOneHundredCharacters()
    {
        Assert.True(HashtagNormalizer.TryNormalize(new string('a', 100), out _));
        Assert.False(HashtagNormalizer.TryNormalize(new string('a', 101), out _));
    }

    [Fact]
    public void NormalizeAll_KeepsTheOrderAndTheFirstSpelling_AndDropsRepeatsOfAnyLetterCase()
    {
        var errors = new ValidationErrors();

        var result = HashtagNormalizer.NormalizeAll(["#JustMakeIt", "creativity", "#justmakeit", "Creativity", "GrowTogether"], errors, "hashtags");

        Assert.Equal(["JustMakeIt", "creativity", "GrowTogether"], result);
        Assert.False(errors.Any);
    }

    [Fact]
    public void NormalizeAll_ReportsEachBadOneAndKeepsTheGoodOnes()
    {
        var errors = new ValidationErrors();

        var result = HashtagNormalizer.NormalizeAll(["fine", "two words", "#"], errors, "hashtags");

        Assert.Equal(["fine"], result);
        Assert.Equal(2, errors.ToDictionary()["hashtags"].Length);
    }

    [Fact]
    public void NormalizeAll_RefusesMoreThanThirty()
    {
        var errors = new ValidationErrors();

        HashtagNormalizer.NormalizeAll(Enumerable.Range(1, 31).Select(i => "tag" + i), errors, "hashtags");

        Assert.True(errors.Any);
        Assert.Empty(HashtagNormalizer.NormalizeAll(null, new ValidationErrors(), "hashtags"));
    }

    [Fact]
    public void ATagColour_IsStableForAName_AnyLetterCase_AndFromThePalette()
    {
        Assert.Equal(TagColors.For("Motivation"), TagColors.For("motivation"));
        Assert.Contains(TagColors.For("Inspiration"), TagColors.Palette);
        Assert.All(TagColors.Palette, colour => Assert.True(TagColors.IsValid(colour)));
    }

    [Theory]
    [InlineData("#1F7A55", true)]
    [InlineData("#abcdef", true)]
    [InlineData("1F7A55", false)]
    [InlineData("#1F7A5", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("green", false)]
    [InlineData(null, false)]
    public void IsValid_IsTheShapeTheDatabaseAccepts(string? colour, bool expected) => Assert.Equal(expected, TagColors.IsValid(colour));

    // --- Platform rules -------------------------------------------------------------------------

    [Fact]
    public void EveryPlatform_HasExactlyOneRule_WithItsOwnName()
    {
        Assert.Equal(Enum.GetValues<SocialPlatform>().Order(), PlatformRules.All.Select(rule => rule.Platform).Order());
        Assert.All(PlatformRules.All, rule =>
        {
            Assert.Equal(rule.Platform, PlatformRules.For(rule.Platform).Platform);
            Assert.NotEmpty(rule.Name);
            Assert.True(rule.CaptionLimit > 0);
        });
    }

    [Theory]
    [InlineData(SocialPlatform.X, 280)]
    [InlineData(SocialPlatform.Instagram, 2200)]
    [InlineData(SocialPlatform.LinkedIn, 3000)]
    [InlineData(SocialPlatform.Threads, 500)]
    public void TheCaptionLimits_AreThePlatformsOwn(SocialPlatform platform, int limit) =>
        Assert.Equal(limit, PlatformRules.For(platform).CaptionLimit);

    [Fact]
    public void OnlySomePlatforms_HaveATitleOrAHashtagLimit()
    {
        Assert.Equal([SocialPlatform.YouTube, SocialPlatform.Pinterest], PlatformRules.All.Where(r => r.TitleLimit is not null).Select(r => r.Platform));
        Assert.Equal([SocialPlatform.Instagram], PlatformRules.All.Where(r => r.MaxHashtags is not null).Select(r => r.Platform));
    }

    [Fact]
    public void CountedLength_IsTheTextAloneOrTheTextABlankLineAndTheHashtagsWithTheirHash()
    {
        Assert.Equal(5, PlatformRules.CountedLength("Hello", []));
        // "Hello" + "\n\n" + "#ab #cde"
        Assert.Equal(5 + 2 + 8, PlatformRules.CountedLength("Hello", ["ab", "cde"]));
    }
}
