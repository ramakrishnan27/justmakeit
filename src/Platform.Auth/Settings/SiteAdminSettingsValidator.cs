using Microsoft.Extensions.Options;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Settings;

public sealed class SiteAdminSettingsValidator : IValidateOptions<SiteAdminSettings>
{
    public ValidateOptionsResult Validate(string? name, SiteAdminSettings options)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(options.Email);
        var hasPassword = !string.IsNullOrEmpty(options.Password);

        if (!hasEmail && !hasPassword)
        {
            return ValidateOptionsResult.Success;
        }

        if (!hasEmail)
        {
            return ValidateOptionsResult.Fail("SiteAdmin:Password is set but SiteAdmin:Email is not. Set the email too, or remove the password.");
        }

        if (!EmailAddressHelper.IsValid(options.Email))
        {
            return ValidateOptionsResult.Fail("SiteAdmin:Email is not a valid email address.");
        }

        if (hasPassword
            && (options.Password.Length < SiteAdminSettings.MinimumPasswordLength
                || options.Password.Length > SiteAdminSettings.MaximumPasswordLength))
        {
            return ValidateOptionsResult.Fail(
                $"SiteAdmin:Password must be between {SiteAdminSettings.MinimumPasswordLength} and {SiteAdminSettings.MaximumPasswordLength} characters.");
        }

        return ValidateOptionsResult.Success;
    }
}
