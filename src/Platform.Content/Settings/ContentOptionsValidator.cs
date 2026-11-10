using Microsoft.Extensions.Options;

namespace Platform.Content.Settings;

public sealed class ContentOptionsValidator : IValidateOptions<ContentOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentOptions options)
    {
        var failures = new List<string>();
        var publishing = options.Publishing;

        Check(failures, publishing.PollSeconds, 1, 3600, "Content:Publishing:PollSeconds");
        Check(failures, publishing.BatchSize, 1, 100, "Content:Publishing:BatchSize");
        Check(failures, publishing.MaxAttempts, 1, 10, "Content:Publishing:MaxAttempts");
        Check(failures, publishing.StaleMinutes, 1, 1440, "Content:Publishing:StaleMinutes");
        Check(failures, publishing.StartupDelaySeconds, 0, 600, "Content:Publishing:StartupDelaySeconds");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(List<string> failures, int value, int min, int max, string key)
    {
        if (value < min || value > max)
        {
            failures.Add($"{key} must be between {min} and {max}.");
        }
    }
}
