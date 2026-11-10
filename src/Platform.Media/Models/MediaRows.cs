namespace Platform.Media.Models;

// One class per table or read, with settable properties for Dapper (a positional record would not match snake_case columns).

/// <summary>A row of <c>media_assets</c> as it is written: everything but the bytes, which are kept by <c>IMediaStorage</c> under <see cref="StorageKey"/>.</summary>
public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? UploadedBy { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;

    /// <summary>The stored text of a <see cref="Models.MediaType"/>.</summary>
    public string MediaType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>Pixels, read from an image's own header; null for anything else or when the header could not be read.</summary>
    public int? Width { get; set; }

    public int? Height { get; set; }
}

/// <summary>A file as the list and the detail read it.</summary>
public sealed class MediaAssetRow
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How many things use the file (<c>media_asset_usages</c>).</summary>
    public int UsageCount { get; set; }
}

/// <summary>One file with its tags and the folders it is in.</summary>
public sealed class MediaAssetDetail
{
    public required MediaAssetRow Asset { get; init; }
    public required IReadOnlyList<MediaAssetTagRow> Tags { get; init; }
    public required IReadOnlyList<Guid> FolderIds { get; init; }
}

/// <summary>What is needed to send a file back: where it is kept and what it is.</summary>
public sealed class MediaFileRecord
{
    public Guid Id { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
}

public sealed class MediaAssetTagRow
{
    public Guid AssetId { get; set; }
    public Guid TagId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class MediaAssetFolderRow
{
    public Guid AssetId { get; set; }
    public Guid FolderId { get; set; }
}

public sealed class MediaFolder
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class MediaFolderRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public int AssetCount { get; set; }
}

public sealed class MediaTagCountRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AssetCount { get; set; }
}

/// <summary>The counts the page shows next to "All Media" and "Uncategorized".</summary>
public sealed record MediaLibraryCounts(int All, int Uncategorized);

/// <summary>What the list is narrowed by. Everything but the workspace is optional.</summary>
public sealed class MediaListCriteria
{
    public required Guid WorkspaceId { get; init; }
    public MediaType? Type { get; init; }
    public Guid? FolderId { get; init; }
    public bool Uncategorized { get; init; }
    public Guid? TagId { get; init; }
    public string? Search { get; init; }

    /// <summary>Inclusive lower bound, in bytes.</summary>
    public long? MinSizeBytes { get; init; }

    /// <summary>Exclusive upper bound, in bytes.</summary>
    public long? MaxSizeBytes { get; init; }

    public DateTimeOffset? From { get; init; }
    public MediaSort Sort { get; init; } = MediaSort.Newest;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 12;
}

public enum MediaSort
{
    Newest,
    Oldest,
    Name,
    Size
}

public sealed class MediaListResult
{
    public required IReadOnlyList<MediaAssetRow> Items { get; init; }
    public required IReadOnlyDictionary<Guid, IReadOnlyList<MediaAssetTagRow>> Tags { get; init; }
    public required IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Folders { get; init; }

    /// <summary>Per type, ignoring the type filter, so the tabs keep their numbers.</summary>
    public required IReadOnlyDictionary<string, int> TypeCounts { get; init; }
}

public enum OrganizeMode
{
    Move,
    Copy
}

public enum OrganizeOutcome
{
    Done,
    AssetNotFound,
    FolderNotFound,
    WorkspaceMismatch
}

public enum DeleteOutcome
{
    Deleted,
    NotFound,
    InUse
}

/// <summary>What deleting a file leaves behind to clean up: the keys of its bytes (the file and any variants).</summary>
public sealed record MediaDeleteResult(DeleteOutcome Outcome, IReadOnlyList<string> StorageKeys);
