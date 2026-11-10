namespace Platform.Media.Interfaces;

/// <summary>
/// Where the bytes of the files are kept. The database holds only a key. Today the keys name files in a folder
/// (<c>LocalDiskMediaStorage</c>); a store in the cloud would implement the same three methods, and nothing else would change.
/// </summary>
public interface IMediaStorage
{
    /// <summary>Keeps everything <paramref name="content"/> holds under <paramref name="key"/> and returns how many bytes that was. A file that was not written whole is not left behind.</summary>
    Task<long> SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <summary>The stored bytes, to read from the start, or null when there is nothing under the key. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes what is under the key. Nothing under it is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
