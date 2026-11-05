using Microsoft.Extensions.Options;

namespace Platform.Auth.Settings;

public sealed class GoogleSettingsValidator : IValidateOptions<GoogleSettings>
{
    public ValidateOptionsResult Validate(string? name, GoogleSettings options)
    {
        var hasId = !string.IsNullOrWhiteSpace(options.ClientId);
        var hasSecret = !string.IsNullOrWhiteSpace(options.ClientSecret);

        if (!hasId && !hasSecret)
        {
            return ValidateOptionsResult.Success;
        }

        if (hasId != hasSecret)
        {
            return ValidateOptionsResult.Fail(
                "Google:ClientId and Google:ClientSecret go together. Set both to turn on Continue with Google, or remove both.");
        }

        if (!string.IsNullOrWhiteSpace(options.RedirectUri)
            && !(Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))))
        {
            return ValidateOptionsResult.Fail(
                "Google:RedirectUri must be an absolute https address (http is allowed only for localhost).");
        }

        return ValidateOptionsResult.Success;
    }
}
