using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Posts;
using Platform.Content.Features.Scheduling;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;

namespace Platform.Content.Tests;

public sealed class ReadinessCopierMapperTests
{
    private static string[] Fields(ContentPostAggregate post) => [.. ScheduleReadiness.Check(post).ToDictionary().Keys.Order()];

    // --- Readiness -----------------------------------------------------------------------------------

    [Fact]
    public void APostWithTextAndAPlatform_IsReady() =>
        Assert.Empty(Fields(TestData.StoredPost(TestData.NewPost(platforms: "instagram"))));

    [Fact]
    public void APostWithNoPlatform_IsNotReady() =>
        Assert.Equal(["platforms"], Fields(TestData.StoredPost(TestData.NewPost())));

    [Fact]
    public void APostWithOnlyATitle_HasNothingToSay()
    {
        var post = TestData.StoredPost(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "Only a title",
            Platforms = [new PlatformTargetInput { Platform = "x" }]
        });

        Assert.Equal(["bodyDoc"], Fields(post));
    }

    [Theory]
    [InlineData("blog_post")]
    [InlineData("video_script")]
    public void ABlogOrAScript_NeedsATitle(string type) =>
        Assert.Equal(["title"], Fields(TestData.StoredPost(TestData.NewPost("Some text", type, title: null, "x"))));

    [Theory]
    [InlineData("social_post")]
    [InlineData("carousel")]
    [InlineData("short_reel")]
    public void OtherKindsNeedNoTitle(string type) =>
        Assert.Empty(Fields(TestData.StoredPost(TestData.NewPost("Some text", type, title: null, "x"))));

    [Fact]
    public void TheText_MustFitEveryPlatformItGoesTo()
    {
        var text = new string('a', 281);
        var post = TestData.StoredPost(TestData.NewPost(text, "social_post", null, "x", "instagram"));

        var errors = ScheduleReadiness.Check(post).ToDictionary()["platforms"];

        Assert.Single(errors);                          // fits Instagram (2200), not X (280)
        Assert.Contains("X: the text is 281", errors[0]);
        Assert.Contains("limit is 280", errors[0]);
    }

    [Fact]
    public void APlatformsOwnCaption_IsWhatIsMeasuredForIt()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", BodyDoc = TestData.ParagraphDoc(new string('a', 500)),
            Platforms = [new PlatformTargetInput { Platform = "x", Caption = "Short caption for X" }]
        };

        Assert.Empty(Fields(TestData.StoredPost(request)));
    }

    [Fact]
    public void TheHashtags_CountTowardsTheLimit()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", BodyDoc = TestData.ParagraphDoc(new string('a', 270)),
            Hashtags = ["growth", "mindset"], Platforms = [new PlatformTargetInput { Platform = "x" }]
        };

        Assert.Equal(["platforms"], Fields(TestData.StoredPost(request)));
    }

    [Fact]
    public void AYouTubeTitle_HasItsOwnLimit()
    {
        var long_title = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "video_script", Title = new string('t', 101), BodyDoc = TestData.ParagraphDoc("x"),
            Platforms = [new PlatformTargetInput { Platform = "youtube" }]
        };

        Assert.Equal(["platforms"], Fields(TestData.StoredPost(long_title)));
    }

    [Fact]
    public void InstagramsHashtagLimit_IsTheSameAsTheMostAPostCanHave_SoAPostThatIsSavedAlwaysFits()
    {
        // The rule exists for the day the two differ; today a post cannot have more hashtags than Instagram takes.
        Assert.Equal(Platform.Content.Features.Hashtags.HashtagNormalizer.MaxPerPost, Platform.Content.Features.Publishing.PlatformRules.For(Platform.Content.Enums.SocialPlatform.Instagram).MaxHashtags);
        var thirty = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", BodyDoc = TestData.ParagraphDoc("x"),
            Hashtags = [.. Enumerable.Range(1, 30).Select(i => "t" + i)], Platforms = [new PlatformTargetInput { Platform = "instagram" }]
        };

        Assert.Empty(Fields(TestData.StoredPost(thirty)));
    }

    // --- Copier ---------------------------------------------------------------------------------------

    [Fact]
    public void ACopy_IsANewDraftThatSaysWhatTheOriginalSays_WithNothingOfItsPublishing()
    {
        var original = TestData.StoredPost(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = "Dream Bigger", BodyDoc = TestData.ParagraphDoc("Text"),
            SeoSlug = "dream-bigger", Hashtags = ["a", "b"], Tags = ["Motivation"],
            Platforms = [new PlatformTargetInput { Platform = "x", Caption = "Own" }],
            Poll = new PollInput { Question = "Q?", Options = ["Yes", "No"] },
            Ctas = [new CtaInput { ButtonText = "Go", TargetUrl = "https://example.com" }],
            Mentions = [new MentionInput { Platform = "x", Name = "Krishna" }],
            Location = new LocationInput { Name = "Chennai", Latitude = 13.08m, Longitude = 80.27m }
        }, "published", TestData.Now.AddDays(-1));
        original.Post.PublishedAt = TestData.Now;
        original.Platforms[0].ExternalPostId = "ext-1";
        original.Platforms[0].PublishStatus = "published";
        var later = TestData.Now.AddHours(3);
        var anotherUser = Guid.NewGuid();

        var copy = PostCopier.Copy(original, anotherUser, later);

        Assert.NotEqual(original.Post.Id, copy.Post.Id);
        Assert.Equal(("draft", "Copy of Dream Bigger", "Text"), (copy.Post.Status, copy.Post.Title, copy.Post.Body));
        Assert.Equal((anotherUser, anotherUser, later, later), (copy.Post.CreatedBy, copy.Post.UpdatedBy, copy.Post.CreatedAt, copy.Post.UpdatedAt));
        Assert.Null(copy.Post.ScheduledAt);
        Assert.Null(copy.Post.PublishedAt);
        Assert.Null(copy.Post.ArchivedAt);
        Assert.Null(copy.Post.SeoSlug);
        Assert.Equal(original.Post.BodyDoc, copy.Post.BodyDoc);
        Assert.Equal(["a", "b"], copy.Hashtags.Select(h => h.Hashtag));
        Assert.Equal(["Motivation"], copy.Tags.Select(t => t.Name));
        Assert.Equal(("x", "Own", "pending"), (copy.Platforms[0].Platform, copy.Platforms[0].PlatformCaption, copy.Platforms[0].PublishStatus));
        Assert.Null(copy.Platforms[0].ExternalPostId);
        Assert.Equal(["Yes", "No"], copy.PollOptions.Select(o => o.OptionText));
        Assert.Equal(copy.Poll!.Id, copy.PollOptions[0].PollId);
        Assert.NotEqual(original.Poll!.Id, copy.Poll.Id);
        Assert.Single(copy.Ctas);
        Assert.Single(copy.Mentions);
        Assert.Equal("Chennai", copy.Location!.LocationName);
        Assert.All(copy.Hashtags.Select(h => h.PostId).Concat(copy.Ctas.Select(c => c.PostId)).Concat(copy.Platforms.Select(p => p.PostId)), id => Assert.Equal(copy.Post.Id, id));
        // The original is untouched.
        Assert.Equal("published", original.Post.Status);
        Assert.Equal("Dream Bigger", original.Post.Title);
    }

    [Fact]
    public void ACopyOfAnUntitledPost_StaysUntitled_AndALongTitleIsCutToTheLimit()
    {
        var untitled = TestData.StoredPost();
        var long_title = TestData.StoredPost(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = new string('t', 300), BodyDoc = TestData.ParagraphDoc("x") });

        Assert.Equal(string.Empty, PostCopier.Copy(untitled, TestData.UserId, TestData.Now).Post.Title);
        Assert.Equal(300, PostCopier.Copy(long_title, TestData.UserId, TestData.Now).Post.Title.Length);
    }

    // --- Mapper ---------------------------------------------------------------------------------------

    [Fact]
    public void AResponse_CarriesTheDocumentAsJson_NotAsAString()
    {
        var response = PostMapper.ToResponse(TestData.StoredPost(TestData.NewPost("Hello", platforms: "x")));

        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.BodyDoc!.Value.ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.Settings.ValueKind);
        Assert.Equal("Hello", response.BodyText);
        Assert.Equal(["x"], response.Platforms.Select(p => p.Platform));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.Platforms[0].Settings.ValueKind);
    }

    private static PostSummary Summary(string type, DateTimeOffset created) =>
        new() { Id = Guid.NewGuid(), WorkspaceId = TestData.WorkspaceId, ContentType = type, Title = "t", Status = "draft", Visibility = "private", CreatedAt = created, UpdatedAt = created };

    [Fact]
    public void APage_HasACountForEveryType_AndAllIsTheirSum()
    {
        var item = Summary("social_post", TestData.Now);
        var result = new PostListResult
        {
            Items = [item],
            Platforms = [new PostPlatformSummary { PostId = item.Id, Platform = "x", PublishStatus = "pending" }],
            Tags = [new PostTagSummary { PostId = item.Id, Id = Guid.NewGuid(), Name = "Motivation", Color = "#1F7A55" }],
            TypeCounts = new Dictionary<string, long> { ["social_post"] = 10, ["blog_post"] = 6, ["carousel"] = 3 }
        };
        var filter = new PostFilter(null, null, null, null, null, null, null, null, 1, 5);

        var page = PostMapper.ToPage(result, filter);

        Assert.Equal(19, page.TotalCount);
        Assert.Equal(4, page.TotalPages);
        Assert.Equal(
            new Dictionary<string, long> { ["social_post"] = 10, ["blog_post"] = 6, ["video_script"] = 0, ["carousel"] = 3, ["short_reel"] = 0, ["all"] = 19 },
            page.TypeCounts);
        Assert.Equal(["x"], page.Items[0].Platforms.Select(p => p.Platform));
        Assert.Equal(["Motivation"], page.Items[0].Tags.Select(t => t.Name));
    }

    [Fact]
    public void WhenATabIsOpen_TheTotalIsThatTabsCount_ButTheTabsKeepTheirNumbers()
    {
        var result = new PostListResult
        {
            Items = [], Platforms = [], Tags = [],
            TypeCounts = new Dictionary<string, long> { ["social_post"] = 10, ["blog_post"] = 6 }
        };

        var page = PostMapper.ToPage(result, new PostFilter(null, "blog_post", null, null, null, null, null, null, 1, 5));

        Assert.Equal(6, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(16, page.TypeCounts["all"]);
    }

    [Fact]
    public void AnEmptyPage_HasZeroPages()
    {
        var page = PostMapper.ToPage(
            new PostListResult { Items = [], Platforms = [], Tags = [], TypeCounts = new Dictionary<string, long>() },
            new PostFilter(null, null, null, null, null, null, null, null, 1, 20));

        Assert.Equal((0, 0), (page.TotalCount, page.TotalPages));
        Assert.All(page.TypeCounts.Values, count => Assert.Equal(0, count));
    }

    // --- Search -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("dream", "dream:*")]
    [InlineData("Dream Bigger", "Dream:* & Bigger:*")]
    [InlineData("  dream,   bigger! ", "dream:* & bigger:*")]
    [InlineData("namस्ते", "namस्ते:*")]
    public void TheSearch_BecomesAPrefixQueryOfItsWordsAndNothingElse(string typed, string expected) =>
        Assert.Equal(expected, PostRepository.ToTsQuery(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    [InlineData("& | ! ( ) :*")]
    public void ASearchWithNoWord_IsNoSearch(string? typed) => Assert.Null(PostRepository.ToTsQuery(typed));

    [Fact]
    public void TsQuerySyntax_TypedByAPerson_NeverReachesTheQuery()
    {
        var query = PostRepository.ToTsQuery("a' | (b & !c) <-> d:* ); DROP TABLE content_posts; --");

        Assert.DoesNotContain("|", query);
        Assert.DoesNotContain("'", query);
        Assert.DoesNotContain("<->", query);
        Assert.DoesNotContain(";", query);
        Assert.DoesNotContain("!", query);
    }

    [Fact]
    public void TheSearch_UsesAtMostEightWords_EachAtMostFiftyCharacters()
    {
        var query = PostRepository.ToTsQuery(string.Join(" ", Enumerable.Range(1, 12).Select(i => "w" + i)));
        var long_word = PostRepository.ToTsQuery(new string('a', 80));

        Assert.Equal(8, query!.Split(" & ").Length);
        Assert.Equal(50 + 2, long_word!.Length);
    }
}
