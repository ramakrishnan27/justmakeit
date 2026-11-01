namespace Platform.Auth.Services.Google;

/// <summary>Who Google says the person is, after the ID token was checked.</summary>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? FullName);

/// <summary>The code exchange or the ID token could not be trusted. The message is safe to log; it holds no token.</summary>
public sealed class GoogleSignInException(string message, Exception? innerException = null) : Exception(message, innerException);

public interface IGoogleOAuthClient
{
    /// <summary>
    /// Trades the one-time code for an ID token over the back channel and checks it: signed by Google, issued
    /// for this client, not expired, carrying the nonce this sign-in started with.
    /// </summary>
    /// <exception cref="GoogleSignInException">Anything about the exchange or the token is wrong.</exception>
    Task<GoogleIdentity> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken cancellationToken);
}
