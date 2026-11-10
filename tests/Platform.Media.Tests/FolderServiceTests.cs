using NSubstitute;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Media.DTOs;
using Platform.Media.Models;
using Platform.Media.Tests.Support;

namespace Platform.Media.Tests;

public sealed class FolderServiceTests
{
    [Fact]
    public async Task TheOverview_HasTheCountsTheFoldersAndTheTags()
    {
        var fixture = new Fixture();
        var folder = new MediaFolderRow { Id = Guid.NewGuid(), Name = "Social Posts", AssetCount = 78 };
        var tag = new MediaTagCountRow { Id = Guid.NewGuid(), Name = "marketing", AssetCount = 72 };
        fixture.Folders.CountsAsync(default, default, default, default).ReturnsForAnyArgs(new MediaLibraryCounts(248, 10));
        fixture.Folders.ListAsync(default, default, default, default).ReturnsForAnyArgs([folder]);
        fixture.Folders.ListTagsAsync(default, default, default, default).ReturnsForAnyArgs([tag]);

        var overview = await fixture.FolderService().GetOverviewAsync(Fixture.WorkspaceId, default);

        Assert.Equal(248, overview.AllCount);
        Assert.Equal(10, overview.UncategorizedCount);
        Assert.Equal(new MediaFolderResponse(folder.Id, "Social Posts", null, 78), Assert.Single(overview.Folders));
        Assert.Equal(new MediaTagCountResponse(tag.Id, "marketing", 72), Assert.Single(overview.Tags));
        await fixture.Folders.Received(1).ListAsync(Fixture.AgencyId, Fixture.UserId, Fixture.WorkspaceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOverview_NeedsAWorkspace_AndThePermissionToSee()
    {
        var fixture = new Fixture();

        var missing = await Assert.ThrowsAsync<BadRequestException>(() => fixture.FolderService().GetOverviewAsync(null, default));
        Assert.Contains("workspaceId", missing.Errors!.Keys);
        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.FolderService([PermissionKeys.MediaLibraryCreate]).GetOverviewAsync(Fixture.WorkspaceId, default));
        Assert.Empty(fixture.Folders.ReceivedCalls());
    }

    [Fact]
    public async Task AFolder_IsCreatedInTheWorkspace_WithATrimmedName()
    {
        var fixture = new Fixture();
        MediaFolder? inserted = null;
        fixture.Folders.InsertAsync(default, default!, default).ReturnsForAnyArgs(call =>
        {
            inserted = call.ArgAt<MediaFolder>(1);
            return Task.FromResult(true);
        });

        var created = await fixture.FolderService().CreateAsync(new CreateFolderRequest(Fixture.WorkspaceId, "  Blog Images "), default);

        Assert.Equal("Blog Images", created.Name);
        Assert.Equal(0, created.AssetCount);
        Assert.Null(created.ParentFolderId);
        Assert.Equal(created.Id, inserted!.Id);
        Assert.Equal(Fixture.AgencyId, inserted.AgencyId);
        Assert.Equal(Fixture.WorkspaceId, inserted.WorkspaceId);
        Assert.Null(inserted.ParentFolderId);
    }

    [Fact]
    public async Task ADuplicateName_IsA409()
    {
        var fixture = new Fixture();
        fixture.Folders.InsertAsync(default, default!, default).ReturnsForAnyArgs(false);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => fixture.FolderService().CreateAsync(new CreateFolderRequest(Fixture.WorkspaceId, "Videos"), default));

        Assert.Equal("folder_exists", exception.Code);
    }

    [Fact]
    public async Task ABadFolderRequest_IsRefusedFieldByField_AndNothingIsWritten()
    {
        var fixture = new Fixture();

        var both = await Assert.ThrowsAsync<BadRequestException>(() => fixture.FolderService().CreateAsync(new CreateFolderRequest(null, "  "), default));
        Assert.Contains("workspaceId", both.Errors!.Keys);
        Assert.Contains("name", both.Errors.Keys);
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.FolderService().CreateAsync(new CreateFolderRequest(Fixture.WorkspaceId, new string('x', 101)), default));
        Assert.Empty(fixture.Folders.ReceivedCalls());
    }

    [Fact]
    public async Task AFolderInAWorkspaceThatIsNotThere_IsNotFound()
    {
        var fixture = new Fixture();
        fixture.Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => fixture.FolderService().CreateAsync(new CreateFolderRequest(Fixture.WorkspaceId, "Videos"), default));

        Assert.Equal("workspace_not_found", exception.Code);
        await fixture.Folders.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default);
    }

    [Fact]
    public async Task MakingAFolder_NeedsMediaLibraryCreate_AndDeletingOneNeedsMediaLibraryDelete()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.FolderService([PermissionKeys.MediaLibraryView, PermissionKeys.MediaLibraryDelete]).CreateAsync(new CreateFolderRequest(Fixture.WorkspaceId, "Videos"), default));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.FolderService([PermissionKeys.MediaLibraryView, PermissionKeys.MediaLibraryCreate]).DeleteAsync(Guid.NewGuid(), default));
        Assert.Empty(fixture.Folders.ReceivedCalls());
    }

    [Fact]
    public async Task ADeletedFolder_IsDeletedForTheCallersOrganization()
    {
        var fixture = new Fixture();
        var id = Guid.NewGuid();
        fixture.Folders.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(true);

        await fixture.FolderService().DeleteAsync(id, default);

        await fixture.Folders.Received(1).DeleteAsync(Fixture.AgencyId, Fixture.UserId, id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADeleteOfAFolderThatIsNotThere_IsNotFound()
    {
        var fixture = new Fixture();
        fixture.Folders.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(false);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => fixture.FolderService().DeleteAsync(Guid.NewGuid(), default));

        Assert.Equal("folder_not_found", exception.Code);
    }
}
