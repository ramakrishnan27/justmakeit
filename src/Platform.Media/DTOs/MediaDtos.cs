using Platform.Helpers.Validation;
using Platform.Media.Models;
using Platform.Media.Services;

namespace Platform.Media.DTOs;

// --- What the API sends --------------------------------------------------------------------------------------------

/// <summary>What the page needs to check a file before it is sent: the size limit and the extensions the file picker offers.</summary>
public sealed record MediaConfigResponse(long MaxFileBytes, IReadOnlyList<string> Extensions);

public sealed record MediaTagRef(Guid Id, string Name);

/// <summary>A file. The bytes are at <c>/v1/media/assets/{id}/file</c>.</summary>
public sealed record MediaAssetResponse(
    Guid Id,
    Guid WorkspaceId,
    string FileName,
    string MimeType,
    string MediaType,
    long SizeBytes,
    int? Width,
    int? Height,
    int UsageCount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MediaTagRef> Tags,
    IReadOnlyList<Guid> FolderIds);

/// <summary>The files per tab. <c>All</c> is the total of the other four.</summary>
public sealed record MediaTypeCounts(int All, int Image, int Video, int Document, int Other);

public sealed record MediaPageResponse(
    IReadOnlyList<MediaAssetResponse> Items, int Page, int PageSize, long TotalCount, int TotalPages, MediaTypeCounts TypeCounts);

public sealed record MediaFolderResponse(Guid Id, string Name, Guid? ParentFolderId, int AssetCount);

public sealed record MediaTagCountResponse(Guid Id, string Name, int AssetCount);

/// <summary>The side panel of the page: the folders and the tags of a workspace, with how many files each holds.</summary>
public sealed record MediaOverviewResponse(
    int AllCount, int UncategorizedCount, IReadOnlyList<MediaFolderResponse> Folders, IReadOnlyList<MediaTagCountResponse> Tags);

// --- What the API receives -----------------------------------------------------------------------------------------

public sealed record CreateFolderRequest(Guid? WorkspaceId, string? Name);

/// <summary>Puts files in a folder (<c>copy</c>: they stay where they are as well) or takes them out of every folder first (<c>move</c>; no folder means Uncategorized).</summary>
public sealed record OrganizeAssetsRequest(Guid[]? AssetIds, Guid? FolderId, string? Mode);

/// <summary>
/// The file as the service gets it: its name, its length as the form said it, and a stream to read it from the start. The controller
/// opens the stream; the service disposes nothing it did not open.
/// </summary>
public sealed record MediaUploadRequest(
    Guid? WorkspaceId, Guid? FolderId, IReadOnlyList<string>? Tags, string? FileName, long Length, Stream? Content);

/// <summary>The list's query: <c>?workspaceId=&amp;type=image&amp;folderId=&amp;tagId=&amp;q=beach&amp;size=small&amp;from=2026-10-01&amp;sort=name&amp;page=2</c>.</summary>
public sealed record MediaListQuery(
    Guid? WorkspaceId = null,
    string? Type = null,
    Guid? FolderId = null,
    bool Uncategorized = false,
    Guid? TagId = null,
    string? Q = null,
    string? Size = null,
    DateTimeOffset? From = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = MediaListQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 60;

    /// <summary>Files under 1 MB, from 1 MB to under 10 MB, and 10 MB and over.</summary>
    public const long OneMegabyte = 1024 * 1024;

    public const long TenMegabytes = 10 * OneMegabyte;

    public static IReadOnlyList<string> Sizes { get; } = ["small", "medium", "large"];

    public static IReadOnlyList<string> Sorts { get; } = ["newest", "oldest", "name", "size"];

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();

        if (WorkspaceId is null || WorkspaceId == Guid.Empty)
        {
            errors.Add("workspaceId", "Choose a workspace.");
        }

        if (Type is not null && !MediaTypes.TryParse(Type, out _))
        {
            errors.Add("type", "Type must be image, video, document or other.");
        }

        if (FolderId is not null && Uncategorized)
        {
            errors.Add("folderId", "Choose a folder or Uncategorized, not both.");
        }

        if (Size is not null && !Sizes.Contains(Size))
        {
            errors.Add("size", "Size must be small, medium or large.");
        }

        if (Sort is not null && !Sorts.Contains(Sort))
        {
            errors.Add("sort", "Sort must be newest, oldest, name or size.");
        }

        if (Q is { Length: > MediaNaming.MaxSearchLength })
        {
            errors.Add("q", $"Search must be at most {MediaNaming.MaxSearchLength} characters.");
        }

        if (Page < 1)
        {
            errors.Add("page", "Page must be at least 1.");
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }

        return errors;
    }

    /// <summary>What the list asks the repository for. Call after <see cref="Validate"/> found nothing.</summary>
    public MediaListCriteria ToCriteria()
    {
        MediaType? type = Type is not null && MediaTypes.TryParse(Type, out var parsed) ? parsed : null;
        var search = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

        return new MediaListCriteria
        {
            WorkspaceId = WorkspaceId!.Value,
            Type = type,
            FolderId = FolderId,
            Uncategorized = Uncategorized,
            TagId = TagId,
            Search = search,
            MinSizeBytes = Size switch { "medium" => OneMegabyte, "large" => TenMegabytes, _ => null },
            MaxSizeBytes = Size switch { "small" => OneMegabyte, "medium" => TenMegabytes, _ => null },
            From = From,
            Sort = Sort switch { "oldest" => MediaSort.Oldest, "name" => MediaSort.Name, "size" => MediaSort.Size, _ => MediaSort.Newest },
            Page = Page,
            PageSize = PageSize
        };
    }
}
