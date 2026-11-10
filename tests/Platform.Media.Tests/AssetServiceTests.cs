using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Media.DTOs;
using Platform.Media.Models;
using Platform.Media.Services;
using Platform.Media.Tests.Support;

namespace Platform.Media.Tests;

public sealed class AssetServiceTests
{
    private static async Task<T> ThrowsWith<T>(Func<Task> action, int status, string code) where T : AppException
    {
        var exception = Assert.IsAssignableFrom<T>(await Record.ExceptionAsync(action));
        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
        return exception;
    }

    private static async Task<BadRequestException> RefusedField(Func<Task> action, string field)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(action);
        Assert.Contains(field, exception.Errors!.Keys);
        return exception;
    }

    // --- Permissions ----------------------------------------------------------------------------

    [Fact]
    public async Task EveryOperation_AsksForItsOwnPermission_AndRefusesWithoutIt()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        var nothing = Array.Empty<string>();
        var viewOnly = new[] { PermissionKeys.MediaLibraryView };
        var createOnly = new[] { PermissionKeys.MediaLibraryCreate };

        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(nothing).ListAsync(new MediaListQuery(Fixture.WorkspaceId), default), 403, "insufficient_permission");
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(nothing).GetAsync(id, default), 403, "insufficient_permission");
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(nothing).OpenFileAsync(id, default), 403, "insufficient_permission");
        Assert.Throws<ForbiddenException>(() => fixture.AssetService(nothing).GetConfig());
        // Reading is not adding, and adding is not reading.
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(viewOnly).UploadAsync(Fixture.Upload(), default), 403, "insufficient_permission");
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(viewOnly).OrganizeAsync(new OrganizeAssetsRequest([id], null, "move"), default), 403, "insufficient_permission");
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService(createOnly).ListAsync(new MediaListQuery(Fixture.WorkspaceId), default), 403, "insufficient_permission");
        // Adding is not deleting.
        await ThrowsWith<ForbiddenException>(() => fixture.AssetService([PermissionKeys.MediaLibraryView, PermissionKeys.MediaLibraryCreate]).DeleteAsync(id, default), 403, "insufficient_permission");

        Assert.Empty(fixture.Storage.ReceivedCalls());
        Assert.Empty(fixture.Assets.ReceivedCalls());
    }

    [Fact]
    public async Task ANoOneWithNoOrganization_IsNotLetIn()
    {
        var service = new MediaAssetService(
            new Fixture().Assets, new Fixture().Folders, new Fixture().Lookups, new Fixture().Storage,
            Microsoft.Extensions.Options.Options.Create(new Platform.Media.Settings.MediaOptions()), new Platform.Helpers.Tenancy.TenantContext(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaAssetService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ListAsync(new MediaListQuery(Fixture.WorkspaceId), default));
    }

    [Fact]
    public void TheConfig_SaysTheLimitInBytes_AndTheExtensions()
    {
        var config = new Fixture().AssetService(maxMegabytes: 25).GetConfig();

        Assert.Equal(25L * 1024 * 1024, config.MaxFileBytes);
        Assert.Contains(".png", config.Extensions);
        Assert.DoesNotContain(".svg", config.Extensions);
    }

    // --- Uploading ------------------------------------------------------------------------------

    [Fact]
    public async Task APng_IsStoredUnderAKeyMadeFromIds_AndRecordedAsWhatItIs()
    {
        var fixture = new Fixture();

        var created = await fixture.AssetService().UploadAsync(Fixture.Upload(Fixture.Png(1920, 1080, 500), "Summer Beach.png", folderId: Fixture.FolderId, tags: ["Summer", "#beach"]), default);

        var inserted = fixture.Inserted!;
        Assert.Equal(Fixture.AgencyId, inserted.AgencyId);
        Assert.Equal(Fixture.WorkspaceId, inserted.WorkspaceId);
        Assert.Equal(Fixture.UserId, inserted.UploadedBy);
        Assert.Equal("Summer Beach.png", inserted.FileName);
        Assert.Equal("image/png", inserted.MimeType);
        Assert.Equal("image", inserted.MediaType);
        Assert.Equal(500, inserted.FileSizeBytes);
        Assert.Equal(1920, inserted.Width);
        Assert.Equal(1080, inserted.Height);
        Assert.Equal($"{Fixture.AgencyId:N}/{inserted.Id:N}.png", inserted.StorageKey);
        Assert.True(StorageKeys.IsValid(inserted.StorageKey));

        Assert.Equal(["Summer", "beach"], fixture.InsertedTags);
        Assert.Equal(Fixture.FolderId, fixture.InsertedFolder);
        Assert.Equal(500, fixture.Stored[inserted.StorageKey].Length);

        Assert.Equal(inserted.Id, created.Id);
        Assert.Equal("image", created.MediaType);
        Assert.Equal(["Summer", "beach"], created.Tags.Select(tag => tag.Name));
        Assert.Equal([Fixture.FolderId], created.FolderIds);
    }

    [Fact]
    public async Task TheStoredCopy_StartsFromTheFirstByte_NotWhereTheHeaderWasRead()
    {
        var fixture = new Fixture();
        var bytes = Fixture.Png(10, 10, 400_000);   // larger than the part that is read to recognize it
        bytes[300_000] = 0x7E;

        await fixture.AssetService().UploadAsync(Fixture.Upload(bytes), default);

        Assert.Equal(bytes, fixture.Stored[fixture.Inserted!.StorageKey]);
    }

    [Fact]
    public async Task WhatTheFileIs_ComesFromTheFile_NotFromItsName()
    {
        var fixture = new Fixture();

        // A picture called "report.pdf" is a picture.
        await fixture.AssetService().UploadAsync(Fixture.Upload(fileName: "report.pdf"), default);

        Assert.Equal("image/png", fixture.Inserted!.MimeType);
        Assert.EndsWith(".png", fixture.Inserted.StorageKey);
        Assert.Equal("report.pdf", fixture.Inserted.FileName);
    }

    [Fact]
    public async Task APath_InTheFileName_IsCutToTheName()
    {
        var fixture = new Fixture();

        await fixture.AssetService().UploadAsync(Fixture.Upload(fileName: "..\\..\\secret\\holiday.png"), default);

        Assert.Equal("holiday.png", fixture.Inserted!.FileName);
        Assert.DoesNotContain("..", fixture.Inserted.StorageKey);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "logo.png")]
    [InlineData("MZ this is a program", "photo.png")]
    [InlineData("just some text", "notes.txt")]
    public async Task AFileOfATypeThatIsNotAllowed_IsRefusedOnTheFileField_AndNothingIsKept(string content, string name)
    {
        var fixture = new Fixture();
        var bytes = Encoding.ASCII.GetBytes(content);

        var exception = await RefusedField(() => fixture.AssetService().UploadAsync(Fixture.Upload(bytes, name), default), "file");

        Assert.Contains("not supported", exception.Errors!["file"][0]);
        Assert.Empty(fixture.Stored);
        Assert.Null(fixture.Inserted);
    }

    [Fact]
    public async Task NoFile_IsRefusedOnTheFileField()
    {
        var fixture = new Fixture();

        await RefusedField(() => fixture.AssetService().UploadAsync(new MediaUploadRequest(Fixture.WorkspaceId, null, null, null, 0, null), default), "file");
        await RefusedField(() => fixture.AssetService().UploadAsync(new MediaUploadRequest(Fixture.WorkspaceId, null, null, "a.png", 0, new MemoryStream()), default), "file");
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefusedBeforeItIsRead()
    {
        var fixture = new Fixture();
        var stream = Substitute.For<Stream>();
        var request = new MediaUploadRequest(Fixture.WorkspaceId, null, null, "big.mp4", 2L * 1024 * 1024 + 1, stream);

        var exception = await RefusedField(() => fixture.AssetService(maxMegabytes: 2).UploadAsync(request, default), "file");

        Assert.Contains("2 MB", exception.Errors!["file"][0]);
        Assert.Empty(stream.ReceivedCalls());
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task AFileExactlyAtTheLimit_IsAccepted()
    {
        var fixture = new Fixture();
        var bytes = Fixture.Png(10, 10, 1024 * 1024);

        await fixture.AssetService(maxMegabytes: 1).UploadAsync(Fixture.Upload(bytes), default);

        Assert.NotNull(fixture.Inserted);
    }

    [Fact]
    public async Task TheWorkspace_IsRequired_AndMustExist()
    {
        var fixture = new Fixture();
        await RefusedField(() => fixture.AssetService().UploadAsync(new MediaUploadRequest(null, null, null, "a.png", 200, new MemoryStream(Fixture.Png())), default), "workspaceId");

        fixture.Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);
        await ThrowsWith<NotFoundException>(() => fixture.AssetService().UploadAsync(Fixture.Upload(), default), 404, "workspace_not_found");
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task TheFolder_MustBeInTheSameWorkspace()
    {
        var fixture = new Fixture();
        fixture.Folders.ExistsInWorkspaceAsync(default, default, default, default, default).ReturnsForAnyArgs(false);

        await ThrowsWith<NotFoundException>(() => fixture.AssetService().UploadAsync(Fixture.Upload(folderId: Fixture.FolderId), default), 404, "folder_not_found");

        await fixture.Folders.Received(1).ExistsInWorkspaceAsync(Fixture.AgencyId, Fixture.UserId, Fixture.FolderId, Fixture.WorkspaceId, Arg.Any<CancellationToken>());
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task BadTags_AreRefusedOnTheTagsField_AndNothingIsKept()
    {
        var fixture = new Fixture();

        await RefusedField(() => fixture.AssetService().UploadAsync(Fixture.Upload(tags: [new string('t', 60)]), default), "tags");
        Assert.Empty(fixture.Stored);
    }

    [Fact]
    public async Task IfTheRowCannotBeWritten_TheBytesAreRemoved_AndTheOriginalErrorIsTheOneReported()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("the database went away");
        fixture.Assets.InsertAsync(default, default!, default!, default, default).ThrowsAsyncForAnyArgs(failure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.AssetService().UploadAsync(Fixture.Upload(), default));

        Assert.Same(failure, thrown);
        var key = Assert.Single(fixture.Stored).Key;
        await fixture.Storage.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IfCleaningUpAlsoFails_TheOriginalErrorStillWins()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("the database went away");
        fixture.Assets.InsertAsync(default, default!, default!, default, default).ThrowsAsyncForAnyArgs(failure);
        fixture.Storage.DeleteAsync(default!, default).ThrowsAsyncForAnyArgs(new IOException("disk"));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.AssetService().UploadAsync(Fixture.Upload(), default));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task IfTheBytesCannotBeKept_NoRowIsWritten()
    {
        var fixture = new Fixture();
        fixture.Storage.SaveAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new IOException("disk full"));

        await Assert.ThrowsAsync<IOException>(() => fixture.AssetService().UploadAsync(Fixture.Upload(), default));

        Assert.Null(fixture.Inserted);
        await fixture.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task ADocumentAndAVideo_AreRecordedWithTheirGroupAndNoSize()
    {
        var fixture = new Fixture();
        byte[] pdf = [.. Encoding.ASCII.GetBytes("%PDF-1.7\n"), .. new byte[100]];
        await fixture.AssetService().UploadAsync(Fixture.Upload(pdf, "brief.pdf"), default);
        Assert.Equal("document", fixture.Inserted!.MediaType);
        Assert.Null(fixture.Inserted.Width);

        byte[] mp4 = [0, 0, 0, 0x18, .. "ftypisom"u8.ToArray(), .. new byte[100]];
        await fixture.AssetService().UploadAsync(Fixture.Upload(mp4, "clip.mp4"), default);
        Assert.Equal("video", fixture.Inserted.MediaType);
        Assert.Equal("video/mp4", fixture.Inserted.MimeType);
        Assert.Null(fixture.Inserted.Height);
    }

    // --- Listing and reading ----------------------------------------------------------------------

    private static MediaAssetRow Row(string name, string type = "image") => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = Fixture.WorkspaceId, FileName = name, MimeType = "image/png", MediaType = type,
        FileSizeBytes = 100, CreatedAt = DateTimeOffset.UnixEpoch
    };

    private static MediaListResult ResultOf(MediaAssetRow[] rows, Dictionary<string, int> counts, Dictionary<Guid, IReadOnlyList<MediaAssetTagRow>>? tags = null) =>
        new() { Items = rows, Tags = tags ?? [], Folders = new Dictionary<Guid, IReadOnlyList<Guid>>(), TypeCounts = counts };

    [Fact]
    public async Task TheList_CarriesTheCountOfEachTab_AndTheTotalIsTheAllTab()
    {
        var fixture = new Fixture();
        var row = Row("a.png");
        var tag = new MediaAssetTagRow { AssetId = row.Id, TagId = Guid.NewGuid(), Name = "Summer" };
        fixture.Assets.ListAsync(default, default, default!, default).ReturnsForAnyArgs(
            ResultOf([row], new() { ["image"] = 25, ["video"] = 3, ["document"] = 2 }, new() { [row.Id] = [tag] }));

        var page = await fixture.AssetService().ListAsync(new MediaListQuery(Fixture.WorkspaceId, PageSize: 12), default);

        Assert.Equal(new MediaTypeCounts(30, 25, 3, 2, 0), page.TypeCounts);
        Assert.Equal(30, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(1, page.Page);
        var item = Assert.Single(page.Items);
        Assert.Equal("a.png", item.FileName);
        Assert.Equal(["Summer"], item.Tags.Select(t => t.Name));
        Assert.Empty(item.FolderIds);
    }

    [Fact]
    public async Task WithATypeChosen_TheTotalIsThatTabsCount_ButTheTabsKeepTheirNumbers()
    {
        var fixture = new Fixture();
        fixture.Assets.ListAsync(default, default, default!, default).ReturnsForAnyArgs(
            ResultOf([Row("v.mp4", "video")], new() { ["image"] = 25, ["video"] = 13 }));

        var page = await fixture.AssetService().ListAsync(new MediaListQuery(Fixture.WorkspaceId, Type: "video", PageSize: 12), default);

        Assert.Equal(13, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(38, page.TypeCounts.All);
        Assert.Equal(25, page.TypeCounts.Image);
    }

    [Fact]
    public async Task AnEmptyLibrary_HasNoPages()
    {
        var fixture = new Fixture();
        fixture.Assets.ListAsync(default, default, default!, default).ReturnsForAnyArgs(ResultOf([], []));

        var page = await fixture.AssetService().ListAsync(new MediaListQuery(Fixture.WorkspaceId), default);

        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.Equal(new MediaTypeCounts(0, 0, 0, 0, 0), page.TypeCounts);
    }

    [Fact]
    public async Task AnInvalidQuery_NeverReachesTheDatabase()
    {
        var fixture = new Fixture();

        await RefusedField(() => fixture.AssetService().ListAsync(new MediaListQuery(), default), "workspaceId");
        await RefusedField(() => fixture.AssetService().ListAsync(new MediaListQuery(Fixture.WorkspaceId, Sort: "x"), default), "sort");

        Assert.Empty(fixture.Assets.ReceivedCalls());
    }

    [Fact]
    public async Task TheListIsAskedForTheCallersOrganization_NeverAnotherOne()
    {
        var fixture = new Fixture();
        fixture.Assets.ListAsync(default, default, default!, default).ReturnsForAnyArgs(ResultOf([], []));

        await fixture.AssetService().ListAsync(new MediaListQuery(Fixture.WorkspaceId, Q: " beach "), default);

        await fixture.Assets.Received(1).ListAsync(
            Fixture.AgencyId, Fixture.UserId, Arg.Is<MediaListCriteria>(c => c.WorkspaceId == Fixture.WorkspaceId && c.Search == "beach"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatIsNotThere_IsNotFound()
    {
        var fixture = new Fixture();
        fixture.Assets.GetAsync(default, default, default, default).ReturnsForAnyArgs((MediaAssetDetail?)null);

        await ThrowsWith<NotFoundException>(() => fixture.AssetService().GetAsync(Guid.NewGuid(), default), 404, "asset_not_found");
    }

    // --- Sending the bytes back ---------------------------------------------------------------------

    [Fact]
    public async Task TheBytes_AreOpenedFromTheKeyOfTheRow()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        var key = $"{Fixture.AgencyId:N}/{id:N}.png";
        fixture.Assets.GetFileAsync(default, default, default, default).ReturnsForAnyArgs(
            new MediaFileRecord { Id = id, StorageKey = key, MimeType = "image/png", FileName = "a.png", MediaType = "image" });
        var stream = new MemoryStream([1, 2, 3]);
        fixture.Storage.OpenReadAsync(key, default).ReturnsForAnyArgs(stream);

        var file = await fixture.AssetService().OpenFileAsync(id, default);

        Assert.Same(stream, file.Content);
        Assert.Equal("image/png", file.MimeType);
        Assert.Equal("a.png", file.FileName);
        await fixture.Assets.Received(1).GetFileAsync(Fixture.AgencyId, Fixture.UserId, id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileOfAnotherOrganization_HasNoRow_SoItsBytesAreNeverOpened()
    {
        var fixture = new Fixture();
        fixture.Assets.GetFileAsync(default, default, default, default).ReturnsForAnyArgs((MediaFileRecord?)null);

        await ThrowsWith<NotFoundException>(() => fixture.AssetService().OpenFileAsync(Guid.NewGuid(), default), 404, "asset_not_found");

        await fixture.Storage.DidNotReceiveWithAnyArgs().OpenReadAsync(default!, default);
    }

    [Fact]
    public async Task ARowWhoseBytesAreGone_IsAMissingFile_NotAServerError()
    {
        var fixture = new Fixture();
        fixture.Assets.GetFileAsync(default, default, default, default).ReturnsForAnyArgs(
            new MediaFileRecord { StorageKey = $"{Fixture.AgencyId:N}/{Guid.NewGuid():N}.png", MimeType = "image/png", FileName = "a.png", MediaType = "image" });
        fixture.Storage.OpenReadAsync(default!, default).ReturnsForAnyArgs((Stream?)null);

        await ThrowsWith<NotFoundException>(() => fixture.AssetService().OpenFileAsync(Guid.NewGuid(), default), 404, "asset_file_missing");
    }

    // --- Deleting --------------------------------------------------------------------------------

    [Fact]
    public async Task ADeletedFile_TakesItsBytes_AndItsVariantsWithIt()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        string[] keys = [$"{Fixture.AgencyId:N}/{id:N}.png", $"{Fixture.AgencyId:N}/{Guid.NewGuid():N}.png"];
        fixture.Assets.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(new MediaDeleteResult(DeleteOutcome.Deleted, keys));

        await fixture.AssetService().DeleteAsync(id, default);

        await fixture.Assets.Received(1).DeleteAsync(Fixture.AgencyId, Fixture.UserId, id, Arg.Any<CancellationToken>());
        foreach (var key in keys)
        {
            await fixture.Storage.Received(1).DeleteAsync(key, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task AFileInUse_IsRefusedWith409_AndItsBytesStay()
    {
        var fixture = new Fixture();
        fixture.Assets.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(new MediaDeleteResult(DeleteOutcome.InUse, []));

        var exception = await ThrowsWith<ConflictException>(() => fixture.AssetService().DeleteAsync(Guid.NewGuid(), default), 409, "asset_in_use");

        Assert.Contains("used by content", exception.Message);
        await fixture.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task ADeleteOfAFileThatIsNotThere_IsNotFound()
    {
        var fixture = new Fixture();
        fixture.Assets.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(new MediaDeleteResult(DeleteOutcome.NotFound, []));

        await ThrowsWith<NotFoundException>(() => fixture.AssetService().DeleteAsync(Guid.NewGuid(), default), 404, "asset_not_found");
        await fixture.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task IfTheBytesCannotBeRemoved_TheDeleteStillSucceeds_BecauseTheRowIsGone()
    {
        var fixture = new Fixture();
        fixture.Assets.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(
            new MediaDeleteResult(DeleteOutcome.Deleted, [$"{Fixture.AgencyId:N}/{Guid.NewGuid():N}.png"]));
        fixture.Storage.DeleteAsync(default!, default).ThrowsAsyncForAnyArgs(new IOException("locked"));

        await fixture.AssetService().DeleteAsync(Guid.NewGuid(), default);
    }

    // --- Organizing -------------------------------------------------------------------------------

    private static OrganizeAssetsRequest Organize(string? mode, Guid? folder = null, params Guid[] ids) => new(ids, folder, mode);

    [Fact]
    public async Task AMoveToAFolder_IsHandedToTheRepository_WithEachFileOnce()
    {
        var fixture = new Fixture();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        fixture.Assets.OrganizeAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(OrganizeOutcome.Done);

        await fixture.AssetService().OrganizeAsync(Organize("move", Fixture.FolderId, a, b, a), default);

        await fixture.Assets.Received(1).OrganizeAsync(
            Fixture.AgencyId, Fixture.UserId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { a, b })),
            Fixture.FolderId, OrganizeMode.Move, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMoveToNoFolder_MakesFilesUncategorized_AndAnEmptyGuidMeansNoFolder()
    {
        var fixture = new Fixture();
        fixture.Assets.OrganizeAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(OrganizeOutcome.Done);

        await fixture.AssetService().OrganizeAsync(Organize("move", null, Guid.NewGuid()), default);
        await fixture.AssetService().OrganizeAsync(Organize("move", Guid.Empty, Guid.NewGuid()), default);

        await fixture.Assets.Received(2).OrganizeAsync(
            Fixture.AgencyId, Fixture.UserId, Arg.Any<IReadOnlyCollection<Guid>>(), null, OrganizeMode.Move, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACopy_NeedsAFolder()
    {
        var fixture = new Fixture();

        await RefusedField(() => fixture.AssetService().OrganizeAsync(Organize("copy", null, Guid.NewGuid()), default), "folderId");
        await RefusedField(() => fixture.AssetService().OrganizeAsync(Organize("copy", Guid.Empty, Guid.NewGuid()), default), "folderId");
        Assert.Empty(fixture.Assets.ReceivedCalls());
    }

    [Fact]
    public async Task TheRequest_IsCheckedBeforeTheDatabaseIsAsked()
    {
        var fixture = new Fixture();
        var service = fixture.AssetService();

        await RefusedField(() => service.OrganizeAsync(new OrganizeAssetsRequest(null, null, "move"), default), "assetIds");
        await RefusedField(() => service.OrganizeAsync(Organize("move"), default), "assetIds");
        await RefusedField(() => service.OrganizeAsync(Organize("move", null, [.. Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())]), default), "assetIds");
        await RefusedField(() => service.OrganizeAsync(Organize("move", null, Guid.Empty), default), "assetIds");
        await RefusedField(() => service.OrganizeAsync(Organize("shuffle", null, Guid.NewGuid()), default), "mode");
        await RefusedField(() => service.OrganizeAsync(Organize(null, null, Guid.NewGuid()), default), "mode");
        await RefusedField(() => service.OrganizeAsync(Organize("MOVE", null, Guid.NewGuid()), default), "mode");

        Assert.Empty(fixture.Assets.ReceivedCalls());
    }

    [Fact]
    public async Task AHundredFiles_AreAllowed()
    {
        var fixture = new Fixture();
        fixture.Assets.OrganizeAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(OrganizeOutcome.Done);

        await fixture.AssetService().OrganizeAsync(Organize("move", Fixture.FolderId, [.. Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())]), default);
    }

    [Theory]
    [InlineData(OrganizeOutcome.AssetNotFound, 404, "asset_not_found")]
    [InlineData(OrganizeOutcome.FolderNotFound, 404, "folder_not_found")]
    [InlineData(OrganizeOutcome.WorkspaceMismatch, 409, "workspace_mismatch")]
    public async Task WhatTheRepositoryRefuses_BecomesAnErrorWithItsOwnCode(OrganizeOutcome outcome, int status, string code)
    {
        var fixture = new Fixture();
        fixture.Assets.OrganizeAsync(default, default, default!, default, default, default).ReturnsForAnyArgs(outcome);

        await ThrowsWith<AppException>(() => fixture.AssetService().OrganizeAsync(Organize("move", Fixture.FolderId, Guid.NewGuid()), default), status, code);
    }
}
