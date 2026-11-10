using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Content.Tests;

public sealed class PostServiceTests : ServiceTestBase
{
    private readonly PostService _service;

    public PostServiceTests() =>
        _service = new PostService(Posts, Lookups, Campaigns, Media, Usage, Tenant, Time, NullLogger<PostService>.Instance);

    private static readonly CancellationToken Ct = CancellationToken.None;

    // --- Permissions ----------------------------------------------------------------------------

    [Fact]
    public async Task EveryMethod_RefusesACallerWithoutThePermission_BeforeTouchingTheDatabase()
    {
        SignIn(); // signed in, holds nothing
        var id = Guid.NewGuid();

        await AssertForbidden(() => _service.ListAsync(new PostListQuery(), Ct));
        await AssertForbidden(() => _service.GetAsync(id, Ct));
        await AssertForbidden(() => _service.CreateAsync(TestData.NewPost(), Ct));
        await AssertForbidden(() => _service.UpdateAsync(id, TestData.NewPost(), Ct));
        await AssertForbidden(() => _service.DuplicateAsync(id, Ct));
        await AssertForbidden(() => _service.DeleteAsync(id, Ct));
        await AssertForbidden(() => _service.ArchiveAsync(id, Ct));
        await AssertForbidden(() => _service.RestoreAsync(id, Ct));
        await AssertForbidden(() => _service.ListRevisionsAsync(id, Ct));

        Assert.Empty(Posts.ReceivedCalls());
        Assert.Empty(Lookups.ReceivedCalls());
    }

    private static async Task AssertForbidden(Func<Task> call)
    {
        var failure = await Assert.ThrowsAsync<ForbiddenException>(call);
        Assert.Equal("insufficient_permission", failure.Code);
    }

    [Fact]
    public async Task APersonWhoMayOnlyRead_CannotWriteOrChangeStatus()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(TestData.NewPost(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ArchiveAsync(id, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.RestoreAsync(id, Ct));
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task NoOrganizationInTheToken_IsUnauthorized()
    {
        // Nothing signed in at all.
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAsync(new PostListQuery(), Ct));
    }

    // --- Listing and reading ------------------------------------------------------------------------

    [Fact]
    public async Task List_ValidatesTheQuery_AndAsksTheRepositoryForTheOrganizationAndUser()
    {
        SignIn(PermissionKeys.ContentStudioView);
        Posts.ListAsync(default, default, default!, default).ReturnsForAnyArgs(new PostListResult
        {
            Items = [], Platforms = [], Tags = [], TypeCounts = new Dictionary<string, long>()
        });

        await Assert.ThrowsAsync<BadRequestException>(() => _service.ListAsync(new PostListQuery(Type: "podcast"), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => _service.ListAsync(new PostListQuery(Page: 0), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => _service.ListAsync(new PostListQuery(PageSize: 101), Ct));
        await Posts.DidNotReceiveWithAnyArgs().ListAsync(default, default, default!, default);

        var page = await _service.ListAsync(new PostListQuery(Type: "blog_post", Status: "draft", Platform: "x", Q: "  dream  ", Page: 2, PageSize: 5), Ct);

        Assert.Equal((2, 5), (page.Page, page.PageSize));
        await Posts.Received(1).ListAsync(
            TestData.AgencyId, TestData.UserId,
            Arg.Is<PostFilter>(f => f.Type == "blog_post" && f.Status == "draft" && f.Platform == "x" && f.Search == "dream" && f.Page == 2 && f.PageSize == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_OfAPostThatIsNotThere_IsNotFound()
    {
        SignIn(PermissionKeys.ContentStudioView);

        var failure = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetAsync(Guid.NewGuid(), Ct));

        Assert.Equal("post_not_found", failure.Code);
    }

    [Fact]
    public async Task Get_ReturnsThePost()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var post = TestData.StoredPost();
        Stored(post);

        var response = await _service.GetAsync(post.Post.Id, Ct);

        Assert.Equal((post.Post.Id, "Hello world"), (response.Id, response.BodyText));
    }

    // --- Creating ------------------------------------------------------------------------------------

    private ContentPostAggregate? CaptureInsert()
    {
        ContentPostAggregate? captured = null;
        Posts.When(r => r.InsertAsync(Arg.Any<Guid>(), Arg.Any<ContentPostAggregate>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.ArgAt<ContentPostAggregate>(1));
        Posts.GetAsync(TestData.AgencyId, TestData.UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => captured is { } c && c.Post.Id == call.ArgAt<Guid>(2) ? c : null);
        return captured;
    }

    [Fact]
    public async Task Create_StoresADraftForTheCaller_AndReturnsItAsSaved()
    {
        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        CaptureInsert();

        var response = await _service.CreateAsync(TestData.NewPost("Dream big", title: "Dream Bigger", platforms: "instagram"), Ct);

        Assert.Equal(("draft", "Dream Bigger", "Dream big"), (response.Status, response.Title, response.BodyText));
        await Posts.Received(1).InsertAsync(
            TestData.UserId,
            Arg.Is<ContentPostAggregate>(a => a.Post.AgencyId == TestData.AgencyId && a.Post.CreatedBy == TestData.UserId && a.Platforms.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithABadRequest_ReportsEveryFieldAndStoresNothing()
    {
        SignIn(PermissionKeys.ContentStudioCreate);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(new SavePostRequest { Visibility = "secret" }, Ct));

        Assert.Equal("validation_failed", failure.Code);
        Assert.Contains("workspaceId", failure.Errors!.Keys);
        Assert.Contains("contentType", failure.Errors.Keys);
        Assert.Contains("visibility", failure.Errors.Keys);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_InAWorkspaceThatIsNotTheirs_IsNotFound_AndStoresNothing()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);

        var failure = await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(TestData.NewPost(), Ct));

        Assert.Equal("workspace_not_found", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_ForACampaignThatDoesNotExist_IsRefused_BecauseThereAreNoCampaignsYet()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", CampaignId = Guid.NewGuid()
        };

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(request, Ct));

        Assert.Equal("campaign_not_available", failure.Code);
        Assert.Contains("campaignId", failure.Errors!.Keys);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_ForAPlatformThatIsNotEnabled_IsRefusedWithThePlatformNamed()
    {
        SignIn(PermissionKeys.ContentStudioCreate);

        var failure = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateAsync(TestData.NewPost(platforms: ["instagram", "tiktok"]), Ct));

        Assert.Equal("platform_not_enabled", failure.Code);
        Assert.Equal(["tiktok is not enabled for your organization."], failure.Errors!["platforms"]);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    // --- Editing --------------------------------------------------------------------------------------

    private SavePostRequest Edit(ContentPostAggregate existing, string text = "Changed") =>
        new() { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc(text), Platforms = [new PlatformTargetInput { Platform = "x" }] };

    [Fact]
    public async Task Update_OfAPostThatIsNotThere_IsNotFound()
    {
        SignIn(PermissionKeys.ContentStudioCreate);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), TestData.NewPost(), Ct));
    }

    [Theory]
    [InlineData("publishing")]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Update_OfAPostThatHasGoneOutOrIsGoingOut_IsRefused(string status)
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost(status: status);
        Stored(post);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(post.Post.Id, Edit(post), Ct));

        Assert.Equal("post_not_editable", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default, default);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("failed")]
    public async Task Update_OfADraftOrAFailedPost_SavesTheEditedPostAgainstTheVersionThePageLoaded(string status)
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost(status: status);
        Stored(post);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Saved);

        await _service.UpdateAsync(post.Post.Id, Edit(post), Ct);

        await Posts.Received(1).UpdateAsync(
            TestData.UserId,
            Arg.Is<ContentPostAggregate>(a => a.Post.Id == post.Post.Id && a.Post.Body == "Changed" && a.Post.UpdatedBy == TestData.UserId),
            post.Post.UpdatedAt,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenSomeoneSavedFirst_IsAConflict()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost();
        Stored(post);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Changed);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(post.Post.Id, Edit(post), Ct));

        Assert.Equal("content_changed", failure.Code);
    }

    [Fact]
    public async Task Update_WhileAPlatformIsPublishing_IsAConflict()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost();
        Stored(post);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Busy);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(post.Post.Id, Edit(post), Ct));

        Assert.Equal("post_publishing", failure.Code);
    }

    [Fact]
    public async Task Update_WithoutSayingWhichVersion_IsABadRequest()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost();
        Stored(post);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.UpdateAsync(post.Post.Id, TestData.NewPost(), Ct));

        Assert.Contains("expectedUpdatedAt", failure.Errors!.Keys);
    }

    [Fact]
    public async Task Update_OfAScheduledPost_MustLeaveItFitToGoOut()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var post = TestData.StoredPost(TestData.NewPost(platforms: "x"), "scheduled", TestData.Now.AddDays(1));
        Stored(post);
        var request = new SavePostRequest { ExpectedUpdatedAt = post.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("Changed") }; // no platform left

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.UpdateAsync(post.Post.Id, request, Ct));

        Assert.Contains("platforms", failure.Errors!.Keys);
        await Posts.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default, default);
    }

    // --- Duplicating ----------------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_StoresANewDraftForTheCaller()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var original = TestData.StoredPost(TestData.NewPost(title: "Dream Bigger", platforms: "x"), "published");
        CaptureInsert();   // first: the later, more specific setup wins for the original
        Stored(original);

        var response = await _service.DuplicateAsync(original.Post.Id, Ct);

        Assert.NotEqual(original.Post.Id, response.Id);
        Assert.Equal(("draft", "Copy of Dream Bigger"), (response.Status, response.Title));
        await Posts.Received(1).InsertAsync(TestData.UserId, Arg.Is<ContentPostAggregate>(a => a.Post.CreatedBy == TestData.UserId), Arg.Any<CancellationToken>());
    }

    // --- Deleting -------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_OfADraft_NeedsTheWritersPermission_NotThePublishers()
    {
        var draft = TestData.StoredPost();
        Stored(draft);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(true);

        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioPublish);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.DeleteAsync(draft.Post.Id, Ct));

        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        await _service.DeleteAsync(draft.Post.Id, Ct);

        await Posts.Received(1).DeleteAsync(
            TestData.AgencyId, TestData.UserId, draft.Post.Id,
            Arg.Is<IReadOnlyCollection<string>>(s => s.Order().SequenceEqual(new[] { "archived", "draft", "failed" })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("archived")]
    public async Task Delete_OfAPostThatWentThroughPublishing_NeedsThePublishersPermission(string status)
    {
        var post = TestData.StoredPost(status: status);
        Stored(post);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(true);

        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.DeleteAsync(post.Post.Id, Ct));

        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioPublish);
        await _service.DeleteAsync(post.Post.Id, Ct);
    }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("publishing")]
    [InlineData("published")]
    public async Task Delete_OfAScheduledOrPublishedPost_IsRefused_ItIsArchivedInstead(string status)
    {
        SignInWithEverything();
        var post = TestData.StoredPost(status: status);
        Stored(post);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(post.Post.Id, Ct));

        Assert.Equal("post_not_deletable", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task Delete_WhenThePostChangedMeanwhile_IsAConflict()
    {
        SignInWithEverything();
        var post = TestData.StoredPost();
        Stored(post);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(false);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(post.Post.Id, Ct));

        Assert.Equal("invalid_transition", failure.Code);
    }

    // --- Archiving and restoring -----------------------------------------------------------------------

    [Theory]
    [InlineData("draft")]
    [InlineData("failed")]
    [InlineData("published")]
    public async Task Archive_IsAllowedFromDraftFailedAndPublished(string status)
    {
        SignIn(PermissionKeys.ContentStudioPublish);
        var post = TestData.StoredPost(status: status);
        Stored(post);
        Posts.ArchiveAsync(default, default, default, default!, default, default).ReturnsForAnyArgs(true);

        await _service.ArchiveAsync(post.Post.Id, Ct);

        await Posts.Received(1).ArchiveAsync(
            TestData.AgencyId, TestData.UserId, post.Post.Id,
            Arg.Is<IReadOnlyCollection<string>>(s => s.Order().SequenceEqual(new[] { "draft", "failed", "published" })),
            TestData.Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("scheduled")]
    [InlineData("publishing")]
    [InlineData("archived")]
    public async Task Archive_IsRefusedFromTheOtherStatuses(string status)
    {
        SignIn(PermissionKeys.ContentStudioPublish);
        var post = TestData.StoredPost(status: status);
        Stored(post);

        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.ArchiveAsync(post.Post.Id, Ct));

        Assert.Equal("invalid_transition", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().ArchiveAsync(default, default, default, default!, default, default);
    }

    [Fact]
    public async Task Restore_OnlyOfAnArchivedPost()
    {
        SignIn(PermissionKeys.ContentStudioPublish);
        var archived = TestData.StoredPost(status: "archived");
        var draft = TestData.StoredPost();
        Stored(archived);
        Stored(draft);
        Posts.RestoreAsync(default, default, default, default).ReturnsForAnyArgs("draft");

        await _service.RestoreAsync(archived.Post.Id, Ct);
        var failure = await Assert.ThrowsAsync<ConflictException>(() => _service.RestoreAsync(draft.Post.Id, Ct));

        Assert.Equal("invalid_transition", failure.Code);
        await Posts.Received(1).RestoreAsync(TestData.AgencyId, TestData.UserId, archived.Post.Id, Arg.Any<CancellationToken>());
    }

    // --- Revisions -------------------------------------------------------------------------------------

    [Fact]
    public async Task Revisions_ArePassedOnNewestFirst_AndAMissingPostIsNotFound()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var id = Guid.NewGuid();
        Posts.ExistsAsync(TestData.AgencyId, TestData.UserId, id, Arg.Any<CancellationToken>()).Returns(true);
        Posts.ListRevisionsAsync(TestData.AgencyId, TestData.UserId, id, Arg.Any<CancellationToken>()).Returns(
        [
            new ContentRevision { RevisionNumber = 2, Title = "Second", BodyLength = 10, ChangedBy = TestData.UserId, CreatedAt = TestData.Now },
            new ContentRevision { RevisionNumber = 1, Title = "First", BodyLength = 5, ChangedBy = null, CreatedAt = TestData.Now.AddHours(-1) }
        ]);

        var revisions = await _service.ListRevisionsAsync(id, Ct);

        Assert.Equal([2, 1], revisions.Select(r => r.RevisionNumber));
        Assert.Null(revisions[1].ChangedBy);
        var failure = await Assert.ThrowsAsync<NotFoundException>(() => _service.ListRevisionsAsync(Guid.NewGuid(), Ct));
        Assert.Equal("post_not_found", failure.Code);
    }
}
