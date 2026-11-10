using Microsoft.Extensions.Options;

namespace Platform.Media.Settings;

public sealed class MediaOptionsValidator : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions options)
    {
        var failures = new List<string>();

        if (options.MaxFileMegabytes is < 1 or > 1024)
        {
            failures.Add("Media:MaxFileMegabytes must be between 1 and 1024.");
        }

        var root = options.Storage.RootPath;
        if (root.Length > 0 && (root.Trim().Length == 0 || root.Contains('\0')))
        {
            failures.Add("Media:Storage:RootPath must be a folder path, or empty for the default.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
