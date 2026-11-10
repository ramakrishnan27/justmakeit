using Microsoft.Extensions.Logging;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;
using Platform.Media.Models;

namespace Platform.Media.Services;

/// <summary>The folders and tags of a workspace.</summary>
public sealed class MediaFolderService(
    IMediaFolderRepository folders,
    IMediaLookupRepository lookups,
    ITenantContext tenant,
    ILogger<MediaFolderService> logger) : IMediaFolderService
{
    public async Task<MediaOverviewResponse> GetOverviewAsync(Guid? workspaceId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryView, "see media");
        if (workspaceId is null || workspaceId == Guid.Empty)
        {
            new ValidationErrors().Add("workspaceId", "Choose a workspace.").ThrowIfAny();
        }

        var workspace = workspaceId!.Value;
        var counts = await folders.CountsAsync(agencyId, userId, workspace, cancellationToken);
        var rows = await folders.ListAsync(agencyId, userId, workspace, cancellationToken);
        var tags = await folders.ListTagsAsync(agencyId, userId, workspace, cancellationToken);

        return new MediaOverviewResponse(
            counts.All,
            counts.Uncategorized,
            rows.Select(row => new MediaFolderResponse(row.Id, row.Name, row.ParentFolderId, row.AssetCount)).ToList(),
            tags.Select(tag => new MediaTagCountResponse(tag.Id, tag.Name, tag.AssetCount)).ToList());
    }

    public async Task<MediaFolderResponse> CreateAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryCreate, "create folders");

        var errors = new ValidationErrors();
        if (request.WorkspaceId is null || request.WorkspaceId == Guid.Empty)
        {
            errors.Add("workspaceId", "Choose a workspace.");
        }

        var name = MediaNaming.NormalizeFolderName(request.Name, errors);
        errors.ThrowIfAny();

        var workspaceId = request.WorkspaceId!.Value;
        if (!await lookups.WorkspaceExistsAsync(agencyId, userId, workspaceId, cancellationToken))
        {
            throw new NotFoundException("That workspace was not found.", "workspace_not_found");
        }

        var folder = new MediaFolder { Id = Guid.NewGuid(), AgencyId = agencyId, WorkspaceId = workspaceId, Name = name! };
        if (!await folders.InsertAsync(userId, folder, cancellationToken))
        {
            throw new ConflictException("This workspace already has a folder with that name.", "folder_exists");
        }

        logger.LogInformation("User {UserId} created media folder {FolderId} in workspace {WorkspaceId}", userId, folder.Id, workspaceId);
        return new MediaFolderResponse(folder.Id, folder.Name, null, 0);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryDelete, "delete folders");

        if (!await folders.DeleteAsync(agencyId, userId, id, cancellationToken))
        {
            throw new NotFoundException("That folder was not found.", "folder_not_found");
        }

        logger.LogInformation("User {UserId} deleted media folder {FolderId}", userId, id);
    }
}
