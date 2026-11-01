using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Platform.Auth.Settings;

namespace Platform.Auth.Services.Google;

/// <summary>
/// The server-to-server half of the sign-in: the one-time code goes to Google's token endpoint together with
/// the client secret and the PKCE verifier, and the ID token that comes back is checked before anyone is trusted.
/// Nothing here is logged: not the code, not the secret, not the token.
/// </summary>
public sealed class GoogleOAuthClient(
    HttpClient http, IOptions<GoogleSettings> options, GoogleIdTokenValidator validator) : IGoogleOAuthClient
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    public async Task<GoogleIdentity> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            throw new GoogleSignInException("Google sign-in is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            })
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new GoogleSignInException("Google could not be reached.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // The body names the problem (for example invalid_grant) but is not echoed anywhere.
                throw new GoogleSignInException($"Google refused the sign-in code (HTTP {(int)response.StatusCode}).");
            }

            TokenResponseBody? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<TokenResponseBody>(cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new GoogleSignInException("Google answered with something that is not JSON.", exception);
            }

            if (string.IsNullOrWhiteSpace(body?.IdToken))
            {
                throw new GoogleSignInException("Google did not return an ID token.");
            }

            return await validator.ValidateAsync(body.IdToken, settings.ClientId, expectedNonce, cancellationToken);
        }
    }

    private sealed record TokenResponseBody([property: JsonPropertyName("id_token")] string? IdToken);
}
