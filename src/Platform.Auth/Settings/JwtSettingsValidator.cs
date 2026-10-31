using System.Text;
using Microsoft.Extensions.Options;

namespace Platform.Auth.Settings;

public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return ValidateOptionsResult.Fail(
                "Jwt:SigningKey is missing. Set it to a random secret of at least 32 bytes with user-secrets " +
                "(dotnet user-secrets --project src/Platform.Api set \"Jwt:SigningKey\" \"<secret>\") " +
                "or the Jwt__SigningKey environment variable.");
        }

        var keyBytes = Encoding.UTF8.GetByteCount(options.SigningKey);
        if (keyBytes < JwtSettings.MinimumSigningKeyBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Jwt:SigningKey is too short: it is {keyBytes} bytes and must be at least {JwtSettings.MinimumSigningKeyBytes}.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            return ValidateOptionsResult.Fail("Jwt:Issuer and Jwt:Audience must not be empty.");
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            return ValidateOptionsResult.Fail("Jwt:AccessTokenMinutes must be between 1 and 60.");
        }

        return ValidateOptionsResult.Success;
    }
}
