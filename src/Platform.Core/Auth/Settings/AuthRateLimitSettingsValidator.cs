using Microsoft.Extensions.Options;

namespace Platform.Auth.Settings;

public sealed class AuthRateLimitSettingsValidator : IValidateOptions<AuthRateLimitSettings>
{
    public ValidateOptionsResult Validate(string? name, AuthRateLimitSettings options)
    {
        if (options.CredentialRequestsPerWindow < 1 || options.SessionRequestsPerWindow < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{AuthRateLimitSettings.SectionName}: the request limits must be at least 1.");
        }

        if (options.WindowSeconds < 1)
        {
            return ValidateOptionsResult.Fail($"{AuthRateLimitSettings.SectionName}:WindowSeconds must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
