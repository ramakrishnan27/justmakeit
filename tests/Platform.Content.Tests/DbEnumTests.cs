using System.Text.RegularExpressions;
using Platform.Content.Data;
using Platform.Content.Enums;

namespace Platform.Content.Tests;

public sealed partial class DbEnumTests
{
    [Fact]
    public void TheWireValues_AreTheSnakeCaseNamesOfTheSpec_NotTheLowerCasedMemberNames()
    {
        Assert.Equal(["social_post", "blog_post", "video_script", "carousel", "short_reel"], DbEnum<ContentType>.All);
        Assert.Equal(["draft", "scheduled", "publishing", "published", "failed", "archived"], DbEnum<ContentStatus>.All);
        Assert.Equal(["public", "private", "unlisted"], DbEnum<ContentVisibility>.All);
        Assert.Equal(["pending", "scheduled", "publishing", "published", "failed"], DbEnum<PublishStatus>.All);
        Assert.Equal(["facebook", "instagram", "x", "youtube", "linkedin", "tiktok", "pinterest", "threads"], DbEnum<SocialPlatform>.All);
        Assert.Equal(["attachment", "cover", "thumbnail", "slide", "video"], DbEnum<MediaRole>.All);
        Assert.Equal("social_post", ContentType.SocialPost.ToWire());
        Assert.Equal("youtube", SocialPlatform.YouTube.ToWire());
    }

    [Fact]
    public void EveryMember_RoundTrips()
    {
        foreach (var type in Enum.GetValues<ContentType>())
        {
            Assert.True(DbEnum<ContentType>.TryParse(type.ToWire(), out var parsed));
            Assert.Equal(type, parsed);
        }

        foreach (var platform in Enum.GetValues<SocialPlatform>())
        {
            Assert.Equal(platform, DbEnum<SocialPlatform>.Parse(platform.ToWire()));
        }
    }

    [Theory]
    [InlineData("SocialPost")]
    [InlineData("SOCIAL_POST")]
    [InlineData("socialpost")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(" social_post")]
    [InlineData(null)]
    public void TryParse_IsStrict_ExactTextOnly(string? text)
    {
        Assert.False(DbEnum<ContentType>.TryParse(text, out var value));
        Assert.Equal(default, value);
    }

    [Fact]
    public void Parse_OfUnknownText_IsABug_NotABadRequest() =>
        Assert.Throws<InvalidOperationException>(() => DbEnum<ContentStatus>.Parse("live"));

    // The CHECK constraints of 014_content_studio.sql and the enums are one list written twice: a value in one and not the other is a
    // post the database refuses or a value the code cannot read. These tests read the script, so either side changing fails here.

    private static string Script()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "Platform.Core", "Infrastructure", "Migrations", "014_content_studio.sql");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("014_content_studio.sql was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"CONSTRAINT\s+(?<name>\w+)\s+CHECK\s*\(\s*\w+\s+IN\s*\((?<values>[^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex InListConstraint();

    private static string[] ConstraintValues(string constraint)
    {
        var match = InListConstraint().Matches(Script()).Single(m => m.Groups["name"].Value == constraint);
        return [.. match.Groups["values"].Value.Split(',').Select(v => v.Trim().Trim('\''))];
    }

    [Theory]
    [InlineData("ck_content_posts_type", typeof(ContentType))]
    [InlineData("ck_content_templates_type", typeof(ContentType))]
    [InlineData("ck_content_posts_status", typeof(ContentStatus))]
    [InlineData("ck_content_posts_visibility", typeof(ContentVisibility))]
    [InlineData("ck_content_post_platforms_platform", typeof(SocialPlatform))]
    [InlineData("ck_content_mentions_platform", typeof(SocialPlatform))]
    [InlineData("ck_content_post_platforms_status", typeof(PublishStatus))]
    [InlineData("ck_content_post_media_role", typeof(MediaRole))]
    public void TheEnum_HasExactlyTheValuesOfTheDatabaseConstraint(string constraint, Type enumType)
    {
        var enumValues = (IReadOnlyList<string>)typeof(DbEnum<>).MakeGenericType(enumType).GetProperty("All")!.GetValue(null)!;

        Assert.Equal(ConstraintValues(constraint).Order(), enumValues.Order());
    }
}
