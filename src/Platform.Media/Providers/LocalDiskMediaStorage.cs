using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Platform.Media.Interfaces;
using Platform.Media.Services;
using Platform.Media.Settings;

namespace Platform.Media.Providers;

/// <summary>
/// Keeps each file as a file in a folder: <c>&lt;root&gt;/&lt;organization id&gt;/&lt;file id&gt;.&lt;extension&gt;</c>. A key must have exactly that shape
/// (<see cref="StorageKeys.IsValid"/>), and the full path is checked to be inside the root as well, so no key can reach another place on
/// the disk. A file is written under a temporary name and moved into place once it is whole.
/// </summary>
public sealed class LocalDiskMediaStorage : IMediaStorage
{
    private readonly string _root;

    public LocalDiskMediaStorage(IOptions<MediaOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.Storage.RootPath;
        _root = Path.GetFullPath(
            configured.Trim().Length == 0 ? Path.Combine("App_Data", "media") : configured.Trim(),
            environment.ContentRootPath);
    }

    /// <summary>The folder the files are in, for the log at start-up and the tests.</summary>
    public string Root => _root;

    public async Task<long> SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".part-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var file = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, 81920, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }

            var written = new FileInfo(temporary).Length;
            File.Move(temporary, path, overwrite: false);
            return written;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathOf(key);
        try
        {
            Stream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            // File.Delete does nothing for a file that is not there, but it does throw when the folder is not there either.
            File.Delete(PathOf(key));
        }
        catch (DirectoryNotFoundException)
        {
            // No folder, so no file: nothing to remove.
        }

        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        if (!StorageKeys.IsValid(key))
        {
            throw new ArgumentException("Not a storage key.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a storage key.", nameof(key));
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless; the original error is the one to report.
        }
    }
}
