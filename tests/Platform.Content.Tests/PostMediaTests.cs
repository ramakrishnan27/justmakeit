using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Media;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Media;
using Platform.Helpers.Validation;

namespace Platform.Content.Tests;

/// <summary>
/// Files of the Media Library on a post: what a request may say (the builder), what each file is for (the roles), what the library
/// has to confirm (the service), what is written, copied and answered, and what the library is told. The library itself is a
/// substitute: it answers for the ids it is asked about, and checks nothing, as the real catalog does.
/// </summary>
public sealed class PostMediaTests : ServiceTestBase
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string[] CreateAndLibrary =
        [PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate, PermissionKeys.MediaLibraryView];

    private readonly PostService _service;

    public PostMediaTests() =>
        _service = new PostService(Posts, Lookups, Campaigns, Media, Usage, Tenant, Time, NullLogger<PostService>.Instance);

    // --- Making files and requests -------------------------------------------------------------------

    private static MediaAssetSummary Asset(string name = "photo.png", string type = "image", Guid? workspace = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), workspace ?? TestData.WorkspaceId, name, type, type == "video" ? "video/mp4" : "image/png", 2048, type == "image" ? 800 : null, type == "image" ? 600 : null);

    private void LibraryHolds(params MediaAssetSummary[] assets) =>
        Media.FindAsync(default, default, default!, default).ReturnsForAnyArgs(call =>
        {
            var asked = call.ArgAt<IReadOnlyCollection<Guid>>(2);
            return Task.FromResult<IReadOnlyList<MediaAssetSummary>>([.. assets.Where(a => asked.Contains(a.Id))]);
        });

    private static SavePostRequest Request(string type = "social_post", params Guid[] assets) =>
        new()
        {
            WorkspaceId = TestData.WorkspaceId,
            ContentType = type,
            Title = "A post",
            BodyDoc = TestData.ParagraphDoc("Hello"),
            Media = [.. assets.Select(id => new MediaInput { AssetId = id })]
        };

    private static ContentPostAggregate Build(SavePostRequest request, out ValidationErrors errors)
    {
        errors = new ValidationErrors();
        return PostBuilder.Build(request, null, TestData.AgencyId, TestData.UserId, TestData.Now, errors);
    }

    private ContentPostAggregate? CaptureInsert()
    {
        ContentPostAggregate? captured = null;
        Posts.When(r => r.InsertAsync(Arg.Any<Guid>(), Arg.Any<ContentPostAggregate>(), Arg.Any<CancellationToken>()))
            .Do(call => captured = call.ArgAt<ContentPostAggregate>(1));
        Posts.GetAsync(TestData.AgencyId, TestData.UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => captured is { } c && c.Post.Id == call.ArgAt<Guid>(2) ? c : null);
        return captured;
    }

    // --- The roles ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ContentType.Carousel, "image,image,video", "slide,slide,slide")]
    [InlineData(ContentType.BlogPost, "image,image,video", "cover,attachment,attachment")]
    [InlineData(ContentType.BlogPost, "video,image", "attachment,cover")]
    [InlineData(ContentType.VideoScript, "image,video,image", "thumbnail,video,attachment")]
    [InlineData(ContentType.ShortReel, "video", "video")]
    [InlineData(ContentType.ShortReel, "video,video", "video,video")]
    [InlineData(ContentType.SocialPost, "image,video", "attachment,attachment")]
    public void EachFilesRole_FollowsTheKindOfContentAndTheOrder(ContentType type, string kinds, string expected) =>
        Assert.Equal(expected.Split(','), MediaRoles.Assign(type, kinds.Split(',')).Select(role => role.ToWire()));

    [Fact]
    public void NoOrderOrMixOfFiles_EverGivesMoreThanOneCoverOrThumbnail_WhichTheTablesIndexWouldRefuse()
    {
        var random = new Random(7);
        string[] kinds = ["image", "video"];

        foreach (var type in Enum.GetValues<ContentType>())
        {
            for (var round = 0; round < 200; round++)
            {
                var mix = Enumerable.Range(0, random.Next(0, 11)).Select(_ => kinds[random.Next(2)]).ToArray();

                var roles = MediaRoles.Assign(type, mix);

                Assert.Equal(mix.Length, roles.Length);
                Assert.True(roles.Count(r => r == MediaRole.Cover) <= 1);
                Assert.True(roles.Count(r => r == MediaRole.Thumbnail) <= 1);
            }
        }
    }

    [Fact]
    public void NoFiles_NoRoles() => Assert.Empty(MediaRoles.Assign(ContentType.Carousel, []));

    // --- What a request may say ----------------------------------------------------------------------

    [Fact]
    public void TheBuilder_KeepsTheOrderOfTheFiles_AndGivesEachADefaultRoleUntilTheLibraryHasBeenAsked()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var post = Build(Request("social_post", a, b), out var errors);

        Assert.False(errors.Any);
        Assert.Equal([a, b], post.Media.Select(m => m.MediaAssetId));
        Assert.Equal([0, 1], post.Media.Select(m => m.DisplayOrder));
        Assert.All(post.Media, m =>
        {
            Assert.Equal("attachment", m.MediaRole);
            Assert.Equal(TestData.AgencyId, m.AgencyId);
            Assert.Equal(post.Post.Id, m.PostId);
        });
    }

    [Fact]
    public void ARequestWithNoFiles_HasNone_AndAnEditThatLeavesThemOutClearsThem()
    {
        Assert.Empty(Build(Request(), out _).Media);

        var existing = Build(Request("social_post", Guid.NewGuid()), out _);
        var errors = new ValidationErrors();
        var edited = PostBuilder.Build(new SavePostRequest { ExpectedUpdatedAt = TestData.Now, BodyDoc = TestData.ParagraphDoc("Hi") }, existing, TestData.AgencyId, TestData.UserId, TestData.Now, errors);

        Assert.Empty(edited.Media);
    }

    [Fact]
    public void ExactlyTheMostFiles_IsFine_AndOneMoreIsRefused()
    {
        var ten = Enumerable.Range(0, MediaPolicy.MaxMedia).Select(_ => Guid.NewGuid()).ToArray();

        var built = Build(Request("carousel", ten), out var fine);
        Assert.Equal(10, built.Media.Count);
        Assert.False(fine.Any);

        Build(Request("carousel", [.. ten, Guid.NewGuid()]), out var tooMany);
        Assert.Contains("media", tooMany.ToDictionary().Keys);
        Assert.Contains("At most 10 files.", tooMany.ToDictionary()["media"]);
    }

    [Fact]
    public void AFileWithoutAnId_IsRefused()
    {
        var request = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x",
            Media = [new MediaInput { AssetId = null }, new MediaInput { AssetId = Guid.Empty }]
        };

        Build(request, out var errors);

        Assert.Equal(2, errors.ToDictionary()["media"].Count(m => m == "A file needs its id."));
    }

    [Fact]
    public void TheSameFileTwice_IsRefused()
    {
        var a = Guid.NewGuid();

        Build(Request("social_post", a, a), out var errors);

        Assert.Contains("The same file is on the post twice.", errors.ToDictionary()["media"]);
    }

    [Fact]
    public void ACaptionIsTrimmed_AndLimitedToTheColumn()
    {
        var a = Guid.NewGuid();
        var ok = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Media = [new MediaInput { AssetId = a, Caption = "  Nice  " }]
        };
        Assert.Equal("Nice", Build(ok, out _).Media.Single().Caption);

        var tooLong = new SavePostRequest
        {
            WorkspaceId = TestData.WorkspaceId, ContentType = "social_post", Title = "x", Media = [new MediaInput { AssetId = a, Caption = new string('x', MediaPolicy.MaxCaptionLength + 1) }]
        };
        Build(tooLong, out var errors);
        Assert.Contains("media", errors.ToDictionary().Keys);
    }

    [Fact]
    public void ThePolicyNumbers_MatchTheDatabase()
    {
        // The column's own limits (014_content_studio.sql): caption at most 2200 characters.
        Assert.Equal(2200, MediaPolicy.MaxCaptionLength);
        Assert.True(MediaPolicy.IsAllowedType("image"));
        Assert.True(MediaPolicy.IsAllowedType("video"));
        Assert.False(MediaPolicy.IsAllowedType("document"));
        Assert.False(MediaPolicy.IsAllowedType("other"));
        Assert.False(MediaPolicy.IsAllowedType(null));
    }

    // --- Saving a post with files --------------------------------------------------------------------

    [Fact]
    public async Task Create_WithFiles_StoresThemWithTheRolesOfTheKindOfContent_AnswersWithTheirDetails_AndTellsTheLibrary()
    {
        SignIn(CreateAndLibrary);
        var cover = Asset("cover.png");
        var second = Asset("second.png");
        LibraryHolds(cover, second);
        CaptureInsert();

        var response = await _service.CreateAsync(Request("blog_post", cover.Id, second.Id), Ct);

        await Posts.Received(1).InsertAsync(
            TestData.UserId,
            Arg.Is<ContentPostAggregate>(a => a.Media.Select(m => m.MediaRole).SequenceEqual(new[] { "cover", "attachment" })
                                              && a.Media.Select(m => m.MediaAssetId).SequenceEqual(new[] { cover.Id, second.Id })),
            Arg.Any<CancellationToken>());
        Assert.Equal(["cover", "attachment"], response.Media.Select(m => m.Role));
        Assert.Equal(["cover.png", "second.png"], response.Media.Select(m => m.FileName));
        Assert.Equal(("image", "image/png", 2048L, 800, 600), (response.Media[0].MediaType, response.Media[0].MimeType, response.Media[0].SizeBytes, response.Media[0].Width, response.Media[0].Height));
        await Usage.Received(1).ReplaceAsync(
            TestData.AgencyId, TestData.UserId, response.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { cover.Id, second.Id }.OrderBy(i => i))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithoutFiles_DoesNotAskTheLibraryOrTellIt()
    {
        SignIn(CreateAndLibrary);
        CaptureInsert();

        var response = await _service.CreateAsync(Request(), Ct);

        Assert.Empty(response.Media);
        await Media.DidNotReceiveWithAnyArgs().FindAsync(default, default, default!, default);
        await Usage.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task AddingAFile_NeedsToBeAbleToLookIntoTheLibrary()
    {
        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        var asset = Asset();
        LibraryHolds(asset);

        var failure = await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(Request("social_post", asset.Id), Ct));

        Assert.Equal("insufficient_permission", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
        await Usage.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task KeepingAFileThatIsAlreadyOnThePost_NeedsNoLibraryPermission()
    {
        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        var asset = Asset();
        LibraryHolds(asset);
        var existing = TestData.StoredPost(Request("social_post", asset.Id));
        Stored(existing);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Saved);
        var edit = new SavePostRequest
        {
            ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("Changed"), Media = [new MediaInput { AssetId = asset.Id }]
        };

        await _service.UpdateAsync(existing.Post.Id, edit, Ct);

        await Posts.Received(1).UpdateAsync(
            TestData.UserId, Arg.Is<ContentPostAggregate>(a => a.Media.Single().MediaAssetId == asset.Id), existing.Post.UpdatedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddingAnotherFileToAPost_NeedsThePermission_EvenWhenOthersAreAlreadyThere()
    {
        SignIn(PermissionKeys.ContentStudioView, PermissionKeys.ContentStudioCreate);
        var kept = Asset();
        var added = Asset();
        LibraryHolds(kept, added);
        var existing = TestData.StoredPost(Request("social_post", kept.Id));
        Stored(existing);
        var edit = new SavePostRequest
        {
            ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("x"),
            Media = [new MediaInput { AssetId = kept.Id }, new MediaInput { AssetId = added.Id }]
        };

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.UpdateAsync(existing.Post.Id, edit, Ct));

        await Posts.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default, default);
    }

    [Fact]
    public async Task AFileTheLibraryDoesNotHave_IsRefused_NamedOnTheMediaField_AndNothingIsStored()
    {
        SignIn(CreateAndLibrary);
        var known = Asset();
        LibraryHolds(known);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(Request("social_post", known.Id, Guid.NewGuid()), Ct));

        Assert.Equal("media_asset_not_found", failure.Code);
        Assert.Contains("media", failure.Errors!.Keys);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task AFileOfAnotherWorkspace_IsRefused()
    {
        SignIn(CreateAndLibrary);
        var elsewhere = Asset(workspace: Guid.NewGuid());
        LibraryHolds(elsewhere);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(Request("social_post", elsewhere.Id), Ct));

        Assert.Equal("media_workspace_mismatch", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("other")]
    public async Task OnlyImagesAndVideosCanBeOnAPost(string kind)
    {
        SignIn(CreateAndLibrary);
        var file = Asset("notes.pdf", kind);
        LibraryHolds(file);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(Request("social_post", file.Id), Ct));

        Assert.Equal("media_type_not_allowed", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task WithNoLibrary_NoFileCanBeAdded()
    {
        SignIn(CreateAndLibrary);
        Media.IsAvailable.Returns(false);

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(Request("social_post", Guid.NewGuid()), Ct));

        Assert.Equal("media_not_available", failure.Code);
        await Posts.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task AFileTheLibraryDoesNotReturn_IsNotAnExcuseToSkipTheCheck_EvenWhenTheListIsShort()
    {
        SignIn(CreateAndLibrary);
        var a = Asset();
        var b = Asset();
        LibraryHolds(a); // b is missing

        await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(Request("social_post", a.Id, b.Id), Ct));
    }

    // --- Telling the library -----------------------------------------------------------------------------

    [Fact]
    public async Task IfTheLibraryCannotBeTold_TheSaveStillSucceeds()
    {
        SignIn(CreateAndLibrary);
        var asset = Asset();
        LibraryHolds(asset);
        CaptureInsert();
        Usage.ReplaceAsync(default, default, default, default!, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("the library is down")));

        var response = await _service.CreateAsync(Request("social_post", asset.Id), Ct);

        Assert.Single(response.Media);
    }

    [Fact]
    public async Task EditingAwayAllTheFiles_TellsTheLibraryTheListIsNowEmpty()
    {
        SignIn(CreateAndLibrary);
        var asset = Asset();
        LibraryHolds(asset);
        var existing = TestData.StoredPost(Request("social_post", asset.Id));
        Stored(existing);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Saved);

        await _service.UpdateAsync(existing.Post.Id, new SavePostRequest { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("x") }, Ct);

        await Usage.Received(1).ReplaceAsync(
            TestData.AgencyId, TestData.UserId, existing.Post.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedSave_DoesNotTellTheLibraryAnything()
    {
        SignIn(CreateAndLibrary);
        var asset = Asset();
        LibraryHolds(asset);
        var existing = TestData.StoredPost(Request("social_post", asset.Id));
        Stored(existing);
        Posts.UpdateAsync(default, default!, default, default).ReturnsForAnyArgs(SaveOutcome.Changed);

        await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(
            existing.Post.Id,
            new SavePostRequest { ExpectedUpdatedAt = existing.Post.UpdatedAt, BodyDoc = TestData.ParagraphDoc("x"), Media = [new MediaInput { AssetId = asset.Id }] },
            Ct));

        await Usage.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task DeletingAPost_TellsTheLibraryItUsesNothingAnymore()
    {
        SignIn(CreateAndLibrary);
        var existing = TestData.StoredPost(Request("social_post", Guid.NewGuid()));
        Stored(existing);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(true);

        await _service.DeleteAsync(existing.Post.Id, Ct);

        await Usage.Received(1).RemoveAllAsync(TestData.AgencyId, TestData.UserId, existing.Post.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IfTheLibraryCannotBeTold_TheDeleteStillSucceeds()
    {
        SignIn(CreateAndLibrary);
        var existing = TestData.StoredPost(Request());
        Stored(existing);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(true);
        Usage.RemoveAllAsync(default, default, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("down")));

        await _service.DeleteAsync(existing.Post.Id, Ct);
    }

    [Fact]
    public async Task APostThatWasNotDeleted_IsStillInUse()
    {
        SignIn(CreateAndLibrary);
        var existing = TestData.StoredPost(Request());
        Stored(existing);
        Posts.DeleteAsync(default, default, default, default!, default).ReturnsForAnyArgs(false);

        await Assert.ThrowsAsync<ConflictException>(() => _service.DeleteAsync(existing.Post.Id, Ct));

        await Usage.DidNotReceiveWithAnyArgs().RemoveAllAsync(default, default, default, default);
    }

    // --- Copying, reading, answering -----------------------------------------------------------------

    [Fact]
    public async Task DuplicatingAPost_CopiesItsFilesInTheSameOrderAndRoles_AndTellsTheLibraryAboutTheCopy()
    {
        SignIn(CreateAndLibrary);
        var a = Asset();
        var b = Asset("clip.mp4", "video");
        LibraryHolds(a, b);
        var source = TestData.StoredPost(Request("short_reel", a.Id, b.Id));
        source.Media[0].MediaRole = "thumbnail";
        source.Media[1].MediaRole = "video";
        source.Media[1].Caption = "The clip";
        CaptureInsert();
        Stored(source);

        var copy = await _service.DuplicateAsync(source.Post.Id, Ct);

        Assert.NotEqual(source.Post.Id, copy.Id);
        Assert.Equal([a.Id, b.Id], copy.Media.Select(m => m.AssetId));
        Assert.Equal(["thumbnail", "video"], copy.Media.Select(m => m.Role));
        Assert.Equal(["", "The clip"], copy.Media.Select(m => m.Caption));
        await Usage.Received(1).ReplaceAsync(TestData.AgencyId, TestData.UserId, copy.Id, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheCopier_GivesTheCopyItsOwnRowsForTheSameFiles()
    {
        var source = TestData.StoredPost(Request("carousel", Guid.NewGuid(), Guid.NewGuid()));
        source.Media[0].Caption = "One";

        var copy = PostCopier.Copy(source, TestData.UserId, TestData.Now);

        Assert.Equal(source.Media.Select(m => m.MediaAssetId), copy.Media.Select(m => m.MediaAssetId));
        Assert.Equal(source.Media.Select(m => m.DisplayOrder), copy.Media.Select(m => m.DisplayOrder));
        Assert.Equal(source.Media.Select(m => m.Caption), copy.Media.Select(m => m.Caption));
        Assert.All(copy.Media, m => Assert.Equal(copy.Post.Id, m.PostId));
        Assert.Empty(copy.Media.Select(m => m.Id).Intersect(source.Media.Select(m => m.Id)));
    }

    [Fact]
    public async Task Get_ShowsEachFilesDetails_InOrder()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var a = Asset("a.png");
        var b = Asset("b.mp4", "video");
        LibraryHolds(a, b);
        var post = TestData.StoredPost(Request("social_post", b.Id, a.Id));
        Stored(post);

        var response = await _service.GetAsync(post.Post.Id, Ct);

        Assert.Equal(["b.mp4", "a.png"], response.Media.Select(m => m.FileName));
        Assert.Equal(["video", "image"], response.Media.Select(m => m.MediaType));
        Assert.Equal([0, 1], response.Media.Select(m => m.DisplayOrder));
    }

    [Fact]
    public async Task Get_NeedsNoLibraryPermission_ToSeeWhatIsOnAPost()
    {
        SignIn(PermissionKeys.ContentStudioView); // not MediaLibraryView
        var a = Asset("a.png");
        LibraryHolds(a);
        var post = TestData.StoredPost(Request("social_post", a.Id));
        Stored(post);

        var response = await _service.GetAsync(post.Post.Id, Ct);

        Assert.Equal("a.png", Assert.Single(response.Media).FileName);
    }

    [Fact]
    public async Task AFileTheLibraryNoLongerReturns_IsStillListedById_SoThePostNeverLosesOneFromWhatThePageSees()
    {
        SignIn(PermissionKeys.ContentStudioView);
        LibraryHolds(); // knows nothing
        var id = Guid.NewGuid();
        var post = TestData.StoredPost(Request("social_post", id));
        Stored(post);

        var media = Assert.Single((await _service.GetAsync(post.Post.Id, Ct)).Media);

        Assert.Equal(id, media.AssetId);
        Assert.Equal(string.Empty, media.FileName);
        Assert.Equal(0, media.SizeBytes);
    }

    [Fact]
    public async Task WithNoLibrary_APostThatHasFilesStillAnswersWithThem()
    {
        SignIn(PermissionKeys.ContentStudioView);
        Media.IsAvailable.Returns(false);
        var id = Guid.NewGuid();
        var post = TestData.StoredPost(Request("social_post", id));
        Stored(post);

        var response = await _service.GetAsync(post.Post.Id, Ct);

        Assert.Equal(id, Assert.Single(response.Media).AssetId);
        await Media.DidNotReceiveWithAnyArgs().FindAsync(default, default, default!, default);
    }

    [Fact]
    public void TheMapper_PutsTheFilesInDisplayOrder_AndSaysNothingMadeUpWithoutDetails()
    {
        var post = TestData.StoredPost(Request("carousel", Guid.NewGuid(), Guid.NewGuid()));
        post.Media[0].DisplayOrder = 1;
        post.Media[1].DisplayOrder = 0;

        var response = PostMapper.ToResponse(post);

        Assert.Equal([post.Media[1].MediaAssetId, post.Media[0].MediaAssetId], response.Media.Select(m => m.AssetId));
        Assert.All(response.Media, m => Assert.Equal((string.Empty, string.Empty, 0L), (m.FileName, m.MimeType, m.SizeBytes)));
    }
}
