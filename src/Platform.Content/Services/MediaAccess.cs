using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Media;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Content.Services;

/// <summary>
/// The checks on the files of a post that need the Media Library: that each is there, belongs to the post's workspace and is an
/// image or a video, and that the person may look into the library to add one. Then each file's role is fixed. The pure part (how
/// many files, none twice) is <c>PostBuilder</c>'s; the library's catalog checks nothing itself ("the caller has already checked").
/// </summary>
public static class MediaAccess
{
    public static async Task EnsureAsync(
        IContentMediaLibrary media,
        ITenantContext tenant,
        ContentPostAggregate post,
        ContentPostAggregate? existing,
        Guid agencyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (post.Media.Count == 0)
        {
            return;
        }

        if (!media.IsAvailable)
        {
            throw Refused("The Media Library is not available.", "media_not_available", "There is no Media Library to take files from.");
        }

        // Keeping a file that is already on the post never needs the library; adding one means looking into it.
        var kept = existing?.Media.Select(m => m.MediaAssetId).ToHashSet() ?? [];
        if (post.Media.Any(m => !kept.Contains(m.MediaAssetId)) && !tenant.HasPermission(PermissionKeys.MediaLibraryView))
        {
            throw new ForbiddenException("Your role is not allowed to add files from the Media Library.", "insufficient_permission");
        }

        var found = (await media.FindAsync(agencyId, userId, [.. post.Media.Select(m => m.MediaAssetId)], cancellationToken))
            .ToDictionary(summary => summary.Id);

        if (post.Media.Any(m => !found.ContainsKey(m.MediaAssetId)))
        {
            throw Refused("A file was not found in the Media Library.", "media_asset_not_found", "A file you chose is no longer in the Media Library.");
        }

        if (post.Media.Any(m => found[m.MediaAssetId].WorkspaceId != post.Post.WorkspaceId))
        {
            throw Refused("A file belongs to another workspace.", "media_workspace_mismatch", "Files must come from the same workspace as the post.");
        }

        if (post.Media.Any(m => !MediaPolicy.IsAllowedType(found[m.MediaAssetId].MediaType)))
        {
            throw Refused("Only images and videos can be added to a post.", "media_type_not_allowed", "Only images and videos can be added to a post.");
        }

        var roles = MediaRoles.Assign(DbEnum<ContentType>.Parse(post.Post.ContentType), [.. post.Media.Select(m => found[m.MediaAssetId].MediaType)]);
        for (var i = 0; i < post.Media.Count; i++)
        {
            post.Media[i].MediaRole = roles[i].ToWire();
        }
    }

    private static BadRequestException Refused(string message, string code, string fieldMessage) =>
        new(message, code, new Dictionary<string, string[]> { ["media"] = [fieldMessage] });
}
