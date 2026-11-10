using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;
using Platform.Infrastructure.Data;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;
using Platform.Media.Models;
using Platform.Media.Settings;

namespace Platform.Media.Services;

/// <summary>
/// The files of the Media Library: the list, one file, uploading, sending the bytes back, organizing into folders and deleting. Every
/// method asks for its permission first. The bytes are kept by <see cref="IMediaStorage"/> and the row by the repository; an upload
/// makes sure that either both exist afterwards or neither does.
/// </summary>
public sealed class MediaAssetService(
    IMediaAssetRepository assets,
    IMediaFolderRepository folders,
    IMediaLookupRepository lookups,
    IMediaStorage storage,
    IOptions<MediaOptions> options,
    ITenantContext tenant,
    ILogger<MediaAssetService> logger) : IMediaAssetService
{
    public const int MaxFilesPerOrganize = 100;

    public MediaConfigResponse GetConfig()
    {
        MediaAccess.Require(tenant, PermissionKeys.MediaLibraryView, "see media");
        return new MediaConfigResponse(options.Value.MaxFileBytes, MediaFileRules.Extensions);
    }

    public async Task<MediaPageResponse> ListAsync(MediaListQuery query, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryView, "see media");
        query.Validate().ThrowIfAny();

        var criteria = query.ToCriteria();
        var result = await assets.ListAsync(agencyId, userId, criteria, cancellationToken);

        var counts = MediaMapper.ToCounts(result.TypeCounts);
        var total = criteria.Type is { } type ? result.TypeCounts.GetValueOrDefault(type.ToDb()) : counts.All;
        var items = result.Items
            .Select(row => MediaMapper.ToResponse(row, result.Tags.GetValueOrDefault(row.Id), result.Folders.GetValueOrDefault(row.Id)))
            .ToList();

        return new MediaPageResponse(items, criteria.Page, criteria.PageSize, total, (int)((total + criteria.PageSize - 1) / criteria.PageSize), counts);
    }

    public async Task<MediaAssetResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryView, "see media");

        var detail = await assets.GetAsync(agencyId, userId, id, cancellationToken)
                     ?? throw new NotFoundException("That file was not found.", "asset_not_found");
        return MediaMapper.ToResponse(detail.Asset, detail.Tags, detail.FolderIds);
    }

    public async Task<MediaAssetResponse> UploadAsync(MediaUploadRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryCreate, "add media");
        var maxBytes = options.Value.MaxFileBytes;

        var errors = new ValidationErrors();
        if (request.WorkspaceId is null || request.WorkspaceId == Guid.Empty)
        {
            errors.Add("workspaceId", "Choose a workspace.");
        }

        if (request.Content is null || request.Length <= 0)
        {
            errors.Add("file", "Choose a file to upload.");
        }
        else if (request.Length > maxBytes)
        {
            errors.Add("file", $"A file can be at most {maxBytes / (1024 * 1024)} MB.");
        }

        var tags = MediaNaming.NormalizeTags(request.Tags, errors);
        errors.ThrowIfAny();

        var workspaceId = request.WorkspaceId!.Value;
        if (!await lookups.WorkspaceExistsAsync(agencyId, userId, workspaceId, cancellationToken))
        {
            throw new NotFoundException("That workspace was not found.", "workspace_not_found");
        }

        if (request.FolderId is { } folderId
            && !await folders.ExistsInWorkspaceAsync(agencyId, userId, folderId, workspaceId, cancellationToken))
        {
            throw new NotFoundException("That folder was not found in this workspace.", "folder_not_found");
        }

        // What the file is comes from the file. The name and the type the browser sent are only ever shown or ignored.
        var content = request.Content!;
        var header = new byte[(int)Math.Min(MediaFileRules.HeaderBytes, request.Length)];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var detected = MediaFileRules.Sniff(header.AsSpan(0, read));
        if (detected is null)
        {
            throw new BadRequestException(
                "One or more fields are invalid.",
                ValidationErrors.Code,
                new Dictionary<string, string[]> { ["file"] = [$"This type of file is not supported. Use {MediaFileRules.AcceptedDescription}."] });
        }

        var size = detected.Type == MediaType.Image ? ImageDimensions.TryRead(detected.MimeType, header.AsSpan(0, read)) : null;

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            WorkspaceId = workspaceId,
            UploadedBy = userId,
            FileName = MediaFileRules.CleanFileName(request.FileName),
            MimeType = detected.MimeType,
            MediaType = detected.Type.ToDb(),
            Width = size?.Width,
            Height = size?.Height
        };
        asset.StorageKey = StorageKeys.For(agencyId, asset.Id, detected.Extension);

        // The header was read from the stream; the stored copy starts from the first byte.
        content.Seek(0, SeekOrigin.Begin);
        asset.FileSizeBytes = await storage.SaveAsync(asset.StorageKey, content, cancellationToken);

        try
        {
            await assets.InsertAsync(userId, asset, tags, request.FolderId, cancellationToken);
        }
        catch
        {
            // The row was not written, so nothing may point at the bytes. Cleaning up must not hide the error that caused it.
            await RemoveBytesAsync(asset.StorageKey);
            throw;
        }

        logger.LogInformation(
            "User {UserId} added media {AssetId} ({MediaType}, {SizeBytes} bytes) to workspace {WorkspaceId}",
            userId, asset.Id, asset.MediaType, asset.FileSizeBytes, workspaceId);

        var created = await assets.GetAsync(agencyId, userId, asset.Id, cancellationToken)
                      ?? throw new NotFoundException("That file was not found.", "asset_not_found");
        return MediaMapper.ToResponse(created.Asset, created.Tags, created.FolderIds);
    }

    public async Task<MediaFileContent> OpenFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryView, "see media");

        var record = await assets.GetFileAsync(agencyId, userId, id, cancellationToken)
                     ?? throw new NotFoundException("That file was not found.", "asset_not_found");
        var stream = await storage.OpenReadAsync(record.StorageKey, cancellationToken);
        if (stream is null)
        {
            // The row is there and the bytes are not: something outside the application removed them. Say so in the log, once per request.
            logger.LogError("Media {AssetId} has no stored file under its key", id);
            throw new NotFoundException("The file is missing from storage.", "asset_file_missing");
        }

        return new MediaFileContent(stream, record.MimeType, record.FileName, record.MediaType);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryDelete, "delete media");

        var result = await assets.DeleteAsync(agencyId, userId, id, cancellationToken);
        switch (result.Outcome)
        {
            case DeleteOutcome.NotFound:
                throw new NotFoundException("That file was not found.", "asset_not_found");
            case DeleteOutcome.InUse:
                throw new ConflictException("This file is used by content and cannot be deleted. Remove it from there first.", "asset_in_use");
        }

        // The row is gone for good; the bytes follow. A failure here leaves an unreferenced file, which is logged, not shown.
        foreach (var key in result.StorageKeys)
        {
            await RemoveBytesAsync(key);
        }

        logger.LogInformation("User {UserId} deleted media {AssetId}", userId, id);
    }

    public async Task OrganizeAsync(OrganizeAssetsRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = MediaAccess.Require(tenant, PermissionKeys.MediaLibraryCreate, "organize media");

        var errors = new ValidationErrors();
        var ids = (request.AssetIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            errors.Add("assetIds", "Choose at least one file.");
        }
        else if (ids.Count > MaxFilesPerOrganize)
        {
            errors.Add("assetIds", $"Choose at most {MaxFilesPerOrganize} files at a time.");
        }
        else if (ids.Contains(Guid.Empty))
        {
            errors.Add("assetIds", "A file id is not valid.");
        }

        OrganizeMode mode = default;
        switch (request.Mode)
        {
            case "move":
                mode = OrganizeMode.Move;
                break;
            case "copy":
                mode = OrganizeMode.Copy;
                if (request.FolderId is null || request.FolderId == Guid.Empty)
                {
                    errors.Add("folderId", "Choose a folder to copy to.");
                }

                break;
            default:
                errors.Add("mode", "Mode must be move or copy.");
                break;
        }

        errors.ThrowIfAny();

        var folderId = request.FolderId == Guid.Empty ? null : request.FolderId;
        var outcome = await assets.OrganizeAsync(agencyId, userId, ids, folderId, mode, cancellationToken);
        switch (outcome)
        {
            case OrganizeOutcome.AssetNotFound:
                throw new NotFoundException("One of the files was not found.", "asset_not_found");
            case OrganizeOutcome.FolderNotFound:
                throw new NotFoundException("That folder was not found.", "folder_not_found");
            case OrganizeOutcome.WorkspaceMismatch:
                throw new ConflictException("Files can only be put in a folder of their own workspace.", "workspace_mismatch");
        }
    }

    private async Task RemoveBytesAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove the stored file {StorageKey}; it is not referenced by any row", key);
        }
    }
}
