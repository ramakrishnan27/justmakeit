using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Platform.Auth.Services.Google;

/// <summary>
/// What the server remembers between sending a person to Google and their coming back: the random
/// <see cref="State"/> echoed in the URL (so a callback that this browser did not start is refused), the
/// <see cref="Nonce"/> that must appear in Google's ID token, the PKCE <see cref="CodeVerifier"/>, and what the
/// person chose on our own form before leaving.
/// </summary>
public sealed record GoogleOAuthState(
    string State,
    string Nonce,
    string CodeVerifier,
    string Intent,
    string? BusinessModel,
    string? OrganizationName,
    string RedirectUri,
    DateTimeOffset ExpiresAt);

public interface IGoogleOAuthStateProtector
{
    string Protect(GoogleOAuthState state);

    /// <summary>The state, or null when the value was changed, was not made by this server, or has expired.</summary>
    GoogleOAuthState? Unprotect(string? protectedValue);
}

/// <summary>
/// Encrypts and signs the state with ASP.NET Core Data Protection, so it can live in a cookie the browser
/// cannot read or alter. The keys are the app's data-protection key ring: with several instances they
/// must share one (a deployment concern for Phase 8).
/// </summary>
public sealed class GoogleOAuthStateProtector(IDataProtectionProvider provider, TimeProvider time) : IGoogleOAuthStateProtector
{
    private const string Purpose = "Platform.Auth.GoogleOAuthState.v1";
    private const int MaxProtectedLength = 4096;

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(GoogleOAuthState state) => _protector.Protect(JsonSerializer.Serialize(state));

    public GoogleOAuthState? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || protectedValue.Length > MaxProtectedLength)
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<GoogleOAuthState>(_protector.Unprotect(protectedValue));
            return state is not null && state.ExpiresAt > time.GetUtcNow() ? state : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
