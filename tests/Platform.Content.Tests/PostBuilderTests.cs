using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Posts;
using Platform.Content.Tests.Support;
using Platform.Helpers.Validation;

namespace Platform.Content.Tests;

public sealed class PostBuilderTests
{
    private static (ContentPostAggregate Post, ValidationErrors Errors) Build(SavePostRequest request, ContentPostAggregate? existing = null)
    {
        var errors = new ValidationErrors();
        var post = PostBuilder.Build(request, existing, TestData.AgencyId, TestData.UserId, TestData.Now, errors);
        return (post, errors);
    }

    private static string[] FieldErrors(ValidationErrors errors, string field) =>
        errors.ToDictionary().GetValueOrDefault(field) ?? [];

    // --- Creating ---------------------------------------------------------------------------------

    [Fact]
    public void ANewPost_IsADraft_ByTheirHand_InTheirWorkspace_WithTheTextDerivedFromTheDocument()
    {
        var (aggregate, errors) = Build(NewRequest(text: "Create. Inspire.", title: "  Dream Bigger  "));

        Assert.False(errors.Any);
        var post = aggregate.Post;
        Assert.Equal(("draft", "social_post", "private", "en", "UTC"), (post.Status, post.ContentType, post.Visibility, post.LanguageCode, post.Timezone));
        Assert.Equal("Dream Bigger", post.Title);
        Assert.Equal("Create. Inspire.", post.Body);
        Assert.Equal("Create. Inspire.", post.Excerpt);
        Assert.NotNull(post.BodyDoc);
        Assert.Equal((TestData.AgencyId, TestData.WorkspaceId, TestData.UserId, TestData.UserId), (post.AgencyId, post.WorkspaceId, post.CreatedBy, post.UpdatedBy));
        Assert.Equal("{}", post.Settings);
        Assert.Empty(post.SeoKeywords);
        Assert.NotEqual(Guid.Empty, post.Id);
    }

    private static SavePostRequest NewRequest(string text = "Hello", string? title = null) => TestData.NewPost(text, title: title);

    [Fact]
    public void ThePlainText_IsNeverTakenFromTheClient_OnlyFromTheDocument()
    {
        // There is no property to send it in: the request has a document and nothing else for the text.
        Assert.DoesNotContain(typeof(SavePostRequest).GetProperties(), property => property.Name is "Body" or "BodyText" or "Excerpt");
    }

    [Fact]
    public void AWorkspaceAndAContentType_AreRequiredWhenCreating()
    {
        var (_, errors) = Build(new SavePostRequest { Title = "x" });

        Assert.NotEmpty(FieldErrors(errors, "workspaceId"));
        Assert.NotEmpty(FieldErrors(errors, "contentType"));
    }

    [Fact]
    public void ATitleOrSomeText_IsRequired()
    {
        var (_, errors) = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post" });

        Assert.NotEmpty(FieldErrors(errors, "content"));
    }

    [Fact]
    public void ATitleAlone_IsEnough()
    {
        var (_, errors) = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = "Only a title" });

        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("podcast")]
    [InlineData("SocialPost")]
    [InlineData("")]
    public void AnUnknownContentType_IsRefused(string type)
    {
        var (_, errors) = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = type, Title = "x" });

        Assert.NotEmpty(FieldErrors(errors, "contentType"));
    }

    [Fact]
    public void ATitle_IsAtMostThreeHundredCharactersOnOneLine()
    {
        Assert.NotEmpty(FieldErrors(Build(NewRequest(title: new string('a', 301))).Errors, "title"));
        Assert.NotEmpty(FieldErrors(Build(NewRequest(title: "two\nlines")).Errors, "title"));
        Assert.Empty(FieldErrors(Build(NewRequest(title: new string('a', 300))).Errors, "title"));
    }

    [Fact]
    public void ABadDocument_IsReportedUnderBodyDoc_AndNoTextIsDerived()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            BodyDoc = TestData.Doc("""{"type":"doc","content":[{"type":"image"}]}""")
        };

        var (aggregate, errors) = Build(request);

        Assert.NotEmpty(FieldErrors(errors, "bodyDoc"));
        Assert.Equal(string.Empty, aggregate.Post.Body);
        Assert.Null(aggregate.Post.BodyDoc);
    }

    [Fact]
    public void VisibilityLanguageAndTimeZone_AreChecked()
    {
        var (_, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Visibility = "secret", LanguageCode = "English", Timezone = "Mars/Olympus"
        });

        Assert.NotEmpty(FieldErrors(errors, "visibility"));
        Assert.NotEmpty(FieldErrors(errors, "languageCode"));
        Assert.NotEmpty(FieldErrors(errors, "timezone"));
    }

    [Fact]
    public void AValidVisibilityLanguageAndTimeZone_AreKept()
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = "x",
            Visibility = "unlisted", LanguageCode = "pt-BR", Timezone = "Asia/Kolkata"
        });

        Assert.False(errors.Any);
        Assert.Equal(("unlisted", "pt-BR", "Asia/Kolkata"), (aggregate.Post.Visibility, aggregate.Post.LanguageCode, aggregate.Post.Timezone));
    }

    [Fact]
    public void Seo_IsCheckedAndTheSlugHasTheShapeTheDatabaseAccepts()
    {
        var (_, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = "x",
            SeoSlug = "Not A Slug", SeoTitle = new string('a', 201), SeoDescription = new string('a', 501),
            SeoKeywords = [.. Enumerable.Range(0, 51).Select(i => "k" + i)]
        });

        Assert.NotEmpty(FieldErrors(errors, "seoSlug"));
        Assert.NotEmpty(FieldErrors(errors, "seoTitle"));
        Assert.NotEmpty(FieldErrors(errors, "seoDescription"));
        Assert.NotEmpty(FieldErrors(errors, "seoKeywords"));
    }

    [Fact]
    public void ASlugAndKeywords_AreTrimmedAndRepeatsDropped()
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "blog_post", Title = "x",
            SeoSlug = "  dream-bigger-2026 ", SeoKeywords = ["growth", " Growth ", "mindset"]
        });

        Assert.False(errors.Any);
        Assert.Equal("dream-bigger-2026", aggregate.Post.SeoSlug);
        Assert.Equal(["growth", "mindset"], aggregate.Post.SeoKeywords);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("3")]
    public void Settings_MustBeAnObject(string json)
    {
        var (_, errors) = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Settings = TestData.Doc(json) });

        Assert.NotEmpty(FieldErrors(errors, "settings"));
    }

    // --- Hashtags, tags -------------------------------------------------------------------------------

    [Fact]
    public void Hashtags_AreNormalizedInOrder_AndTagsGetAColourByName()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Hashtags = ["#JustMakeIt", "creativity", "justmakeit"], Tags = ["Motivation", "motivation", " Inspiration "]
        };

        var (aggregate, errors) = Build(request);

        Assert.False(errors.Any);
        Assert.Equal(["JustMakeIt", "creativity"], aggregate.Hashtags.Select(h => h.Hashtag));
        Assert.Equal([0, 1], aggregate.Hashtags.Select(h => h.DisplayOrder));
        Assert.Equal(["Motivation", "Inspiration"], aggregate.Tags.Select(t => t.Name));
        Assert.All(aggregate.Tags, tag => Assert.Matches("^#[0-9A-F]{6}$", tag.Color));
        Assert.All(aggregate.Hashtags, h => Assert.Equal(aggregate.Post.Id, h.PostId));
    }

    [Fact]
    public void TooManyTagsOrABadTagName_IsRefused()
    {
        var many = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Tags = [.. Enumerable.Range(0, 21).Select(i => "t" + i)] });
        var bad = Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Tags = [new string('a', 51)] });

        Assert.NotEmpty(FieldErrors(many.Errors, "tags"));
        Assert.NotEmpty(FieldErrors(bad.Errors, "tags"));
    }

    // --- Platforms --------------------------------------------------------------------------------------

    [Fact]
    public void ThePlatforms_ArePendingForADraft_WithTheirOwnCaptionAndTitle()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Platforms =
            [
                new PlatformTargetInput { Platform = "instagram", Caption = "  Own caption ", Title = "Own title", Settings = TestData.Doc("""{"firstComment":"hi"}""") },
                new PlatformTargetInput { Platform = "x", Caption = "   " }
            ]
        };

        var (aggregate, errors) = Build(request);

        Assert.False(errors.Any);
        Assert.Equal(["instagram", "x"], aggregate.Platforms.Select(p => p.Platform));
        Assert.Equal(["pending", "pending"], aggregate.Platforms.Select(p => p.PublishStatus));
        Assert.Equal("Own caption", aggregate.Platforms[0].PlatformCaption);
        Assert.Equal("Own title", aggregate.Platforms[0].PlatformTitle);
        Assert.Equal("""{"firstComment":"hi"}""", aggregate.Platforms[0].PlatformSettings);
        Assert.Null(aggregate.Platforms[1].PlatformCaption);
        Assert.All(aggregate.Platforms, p => Assert.Null(p.SocialAccountId));
    }

    [Fact]
    public void AnUnknownPlatformOrOneListedTwice_IsRefused()
    {
        var unknown = Build(NewRequestWithPlatforms("myspace"));
        var twice = Build(NewRequestWithPlatforms("x", "x"));

        Assert.NotEmpty(FieldErrors(unknown.Errors, "platforms"));
        Assert.NotEmpty(FieldErrors(twice.Errors, "platforms"));
    }

    private static SavePostRequest NewRequestWithPlatforms(params string[] platforms) =>
        TestData.NewPost("Hello", platforms: platforms);

    // --- Poll, buttons, mentions, location -------------------------------------------------------------

    [Fact]
    public void APoll_HasAQuestionAndTwoToFourDifferentOptions()
    {
        PollInput Poll(params string[] options) => new() { Question = "Pick one?", Options = options };
        SavePostRequest With(PollInput poll) => new() { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Poll = poll };

        Assert.False(Build(With(Poll("Yes", "No"))).Errors.Any);
        Assert.False(Build(With(Poll("a", "b", "c", "d"))).Errors.Any);
        Assert.NotEmpty(FieldErrors(Build(With(Poll("only one"))).Errors, "poll"));
        Assert.NotEmpty(FieldErrors(Build(With(Poll("a", "b", "c", "d", "e"))).Errors, "poll"));
        Assert.NotEmpty(FieldErrors(Build(With(Poll("Same", "same"))).Errors, "poll"));
        Assert.NotEmpty(FieldErrors(Build(With(new PollInput { Question = "", Options = ["a", "b"] })).Errors, "poll"));
        Assert.NotEmpty(FieldErrors(Build(With(new PollInput { Question = "q", Options = ["a", "b"], EndsAt = TestData.Now.AddMinutes(-1) })).Errors, "poll"));
    }

    [Fact]
    public void APoll_IsBuiltWithItsOptionsInOrder()
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Poll = new PollInput { Question = " Pick one? ", Options = ["Yes", "No"], EndsAt = TestData.Now.AddDays(1) }
        });

        Assert.False(errors.Any);
        Assert.Equal("Pick one?", aggregate.Poll!.Question);
        Assert.Equal(["Yes", "No"], aggregate.PollOptions.Select(o => o.OptionText));
        Assert.All(aggregate.PollOptions, o => Assert.Equal(aggregate.Poll.Id, o.PollId));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/x?y=1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("example.com", false)]
    [InlineData("https://exa mple.com", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("", false)]
    public void AButtonAddress_MustBeAWebAddress(string url, bool valid)
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Ctas = [new CtaInput { ButtonText = "Learn more", TargetUrl = url }]
        });

        Assert.Equal(valid, !errors.Any);
        Assert.Equal(valid ? 1 : 0, aggregate.Ctas.Count);
    }

    [Fact]
    public void AtMostThreeButtons_AndTheirTextIsChecked()
    {
        CtaInput Cta(string text = "Go") => new() { ButtonText = text, TargetUrl = "https://example.com" };
        SavePostRequest With(params CtaInput[] ctas) => new() { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Ctas = [.. ctas] };

        Assert.False(Build(With(Cta(), Cta(), Cta())).Errors.Any);
        Assert.NotEmpty(FieldErrors(Build(With(Cta(), Cta(), Cta(), Cta())).Errors, "ctas"));
        Assert.NotEmpty(FieldErrors(Build(With(Cta(""))).Errors, "ctas"));
        Assert.NotEmpty(FieldErrors(Build(With(Cta(new string('a', 61)))).Errors, "ctas"));
    }

    [Fact]
    public void AMention_NeedsAPlatformAndAName_AndTheSamePersonIsListedOnce()
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Mentions = [new MentionInput { Platform = "x", Name = "Krishna" }, new MentionInput { Platform = "x", Name = "krishna" }, new MentionInput { Platform = "linkedin", Name = "Krishna" }]
        });

        Assert.False(errors.Any);
        Assert.Equal(["x", "linkedin"], aggregate.Mentions.Select(m => m.Platform));
        Assert.NotEmpty(FieldErrors(Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Mentions = [new MentionInput { Platform = "myspace", Name = "A" }] }).Errors, "mentions"));
        Assert.NotEmpty(FieldErrors(Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Mentions = [new MentionInput { Platform = "x", Name = "" }] }).Errors, "mentions"));
    }

    [Theory]
    [InlineData(13.0827, 80.2707, true)]
    [InlineData(null, null, true)]
    [InlineData(13.0827, null, false)]
    [InlineData(91.0, 10.0, false)]
    [InlineData(10.0, 181.0, false)]
    public void ALocation_HasBothCoordinatesOrNeither_InRange(double? latitude, double? longitude, bool valid)
    {
        var (aggregate, errors) = Build(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Location = new LocationInput { Name = "Chennai", Latitude = (decimal?)latitude, Longitude = (decimal?)longitude }
        });

        Assert.Equal(valid, !errors.Any);
        Assert.Equal("Chennai", aggregate.Location!.LocationName);
    }

    [Fact]
    public void ALocationWithNoName_IsRefused() =>
        Assert.NotEmpty(FieldErrors(Build(new SavePostRequest { WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Location = new LocationInput { Name = " " } }).Errors, "location"));

    // --- Editing --------------------------------------------------------------------------------------------

    [Fact]
    public void AnEdit_MustSayWhichVersionItIsEditing()
    {
        var existing = TestData.StoredPost();

        var (_, errors) = Build(NewRequest("Changed"), existing);

        Assert.NotEmpty(FieldErrors(errors, "expectedUpdatedAt"));
    }

    [Fact]
    public void AnEdit_KeepsWhatCannotChange_AndTakesTheEditorAsTheUpdater()
    {
        var existing = TestData.StoredPost();
        var otherEditor = Guid.NewGuid();
        var request = new SavePostRequest { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("Changed"), WorkspaceId = Guid.NewGuid(), Title = "New title" };
        var errors = new ValidationErrors();

        var edited = PostBuilder.Build(request, existing, TestData.AgencyId, otherEditor, TestData.Now.AddMinutes(5), errors);

        Assert.False(errors.Any);
        Assert.Equal(existing.Post.Id, edited.Post.Id);
        Assert.Equal(existing.Post.WorkspaceId, edited.Post.WorkspaceId);   // a post stays in its workspace
        Assert.Equal(existing.Post.CreatedBy, edited.Post.CreatedBy);
        Assert.Equal(existing.Post.CreatedAt, edited.Post.CreatedAt);
        Assert.Equal(existing.Post.ContentType, edited.Post.ContentType);   // not sent: kept
        Assert.Equal(otherEditor, edited.Post.UpdatedBy);
        Assert.Equal("Changed", edited.Post.Body);
        Assert.Equal("New title", edited.Post.Title);
        Assert.Equal("Hello world", existing.Post.Body);                    // the original is untouched
    }

    [Fact]
    public void AnEdit_OfAScheduledPost_KeepsItsScheduleAndItsPlatformsStayScheduled()
    {
        var when = TestData.Now.AddDays(2);
        var existing = TestData.StoredPost(TestData.NewPost(platforms: "x"), "scheduled", when);
        var request = new SavePostRequest
        {
            ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("Changed"),
            ScheduledAt = TestData.Now.AddDays(30), Platforms = [new PlatformTargetInput { Platform = "x" }, new PlatformTargetInput { Platform = "instagram" }]
        };

        var (edited, errors) = Build(request, existing);

        Assert.False(errors.Any);
        Assert.Equal(when, edited.Post.ScheduledAt);
        Assert.Equal(["scheduled", "scheduled"], edited.Platforms.Select(p => p.PublishStatus));
        Assert.All(edited.Platforms, p => Assert.Equal(when, p.ScheduledAt));
    }

    [Fact]
    public void AnEdit_OfADraft_SetsTheDateTheDraftIsMeantFor()
    {
        var existing = TestData.StoredPost();
        var when = TestData.Now.AddDays(3);

        var (edited, _) = Build(new SavePostRequest { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("x"), ScheduledAt = when }, existing);

        Assert.Equal(when, edited.Post.ScheduledAt);
        Assert.Equal("draft", edited.Post.Status);
    }

    [Fact]
    public void AnEdit_ReplacesTheChildren_WhatItLeavesOutIsCleared()
    {
        var existing = TestData.StoredPost(new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", BodyDoc = TestData.ParagraphDoc("x"),
            Hashtags = ["a"], Ctas = [new CtaInput { ButtonText = "Go", TargetUrl = "https://example.com" }]
        });

        var (edited, errors) = Build(new SavePostRequest { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("x") }, existing);

        Assert.False(errors.Any);
        Assert.Empty(edited.Hashtags);
        Assert.Empty(edited.Ctas);
    }
}
