using Platform.Media.DTOs;

namespace Platform.Media.Interfaces;

public interface IMediaAssetService
{
    /// <summary>What the page needs to check a file before sending it.</summary>
    MediaConfigResponse GetConfig();

    Task<MediaPageResponse> ListAsync(MediaListQuery query, CancellationToken cancellationToken);

    Task<MediaAssetResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Checks the file, keeps its bytes and writes its row. Either both exist afterwards or neither does.</summary>
    Task<MediaAssetResponse> UploadAsync(MediaUploadRequest request, CancellationToken cancellationToken);

    /// <summary>The stored bytes of a file. The caller disposes <see cref="MediaFileContent.Content"/>.</summary>
    Task<MediaFileContent> OpenFileAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task OrganizeAsync(OrganizeAssetsRequest request, CancellationToken cancellationToken);
}

/// <summary>A file to send back: its bytes, and what it is, for the headers.</summary>
public sealed record MediaFileContent(Stream Content, string MimeType, string FileName, string MediaType);

public interface IMediaFolderService
{
    /// <summary>The folders and tags of a workspace with their counts, and the counts of "All Media" and "Uncategorized".</summary>
    Task<MediaOverviewResponse> GetOverviewAsync(Guid? workspaceId, CancellationToken cancellationToken);

    Task<MediaFolderResponse> CreateAsync(CreateFolderRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a folder and the folders in it. The files stay.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
