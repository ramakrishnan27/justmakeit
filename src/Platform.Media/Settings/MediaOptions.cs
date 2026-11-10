namespace Platform.Media.Settings;

/// <summary>The <c>Media</c> section of the configuration.</summary>
public sealed class MediaOptions
{
    public const string SectionName = "Media";

    /// <summary>The largest file accepted, in megabytes. The database holds at most 2 GB per file.</summary>
    public int MaxFileMegabytes { get; set; } = 100;

    public StorageOptions Storage { get; set; } = new();

    public long MaxFileBytes => MaxFileMegabytes * 1024L * 1024L;
}

public sealed class StorageOptions
{
    /// <summary>
    /// The folder the files are kept in. Empty means <c>App_Data/media</c> under the application's content root. A relative path is
    /// read from the content root too. Back this folder up with the database: a row without its file shows a broken picture.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
