using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Services.Google;

/// <summary>
/// The error codes a failed Google sign-in sends back to the page as <c>?error=</c>. They carry no detail
/// beyond what the person is about to be told.
/// </summary>
public static class GoogleSignInErrors
{
    public const string Unavailable = "google_unavailable";
    public const string InvalidRequest = "google_invalid_request";
    public const string Denied = "google_denied";
    public const string Failed = "google_failed";
    public const string SessionExpired = "google_state_invalid";
    public const string EmailUnverified = "google_email_unverified";
    public const string EmailTaken = "google_email_taken";
    public const string NoAccount = "google_no_account";
    public const string NoOrganization = "google_no_organization";

    /// <summary>The person's own account was deactivated by an organization admin.</summary>
    public const string AccountDeactivated = "google_account_deactivated";

    /// <summary>The whole organization was deactivated by the Site admin.</summary>
    public const string OrganizationDeactivated = "google_organization_deactivated";
}

/// <summary>Where to send the browser, and the encrypted state to keep in a short-lived cookie meanwhile.</summary>
public sealed record GoogleSignInStart(string AuthorizationUrl, string StateCookieValue, DateTimeOffset ExpiresAt);

/// <summary>What Google sent back to the callback address.</summary>
public sealed record GoogleCallback(string? Code, string? State, string? Error);

/// <summary>
/// Either a signed-in session, or an error code for the page. <see cref="Intent"/> says which page (login or
/// register) the person started from, so an error returns them to the same form.
/// </summary>
public sealed record GoogleSignInOutcome(AuthResult? Session, string? ErrorCode, string Intent);

public interface IGoogleSignInService
{
    bool Enabled { get; }

    /// <summary>Builds the Google address to send the person to, and the state to remember until they return.</summary>
    GoogleSignInStart Begin(GoogleStartRequest request, string defaultRedirectUri);

    /// <summary>Finishes the sign-in from Google's callback and the state cookie. Never throws for a person's mistake: it returns an error code.</summary>
    Task<GoogleSignInOutcome> CompleteAsync(GoogleCallback callback, string? stateCookie, CancellationToken cancellationToken);
}

public sealed class GoogleSignInService(
    IGoogleOAuthClient google,
    IGoogleOAuthStateProtector stateProtector,
    IAccountRepository accounts,
    IAuthService auth,
    IOptions<GoogleSettings> options,
    TimeProvider time,
    ILogger<GoogleSignInService> logger) : IGoogleSignInService
{
    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private const int MaxFullNameLength = FieldRules.MaxNameLength;

    public bool Enabled => options.Value.Enabled;

    public GoogleSignInStart Begin(GoogleStartRequest request, string defaultRedirectUri)
    {
        if (!Enabled)
        {
            throw new NotFoundException("Sign-in with Google is not set up.", GoogleSignInErrors.Unavailable);
        }

        request.Validate().ThrowIfAny();

        var settings = options.Value;
        var redirectUri = string.IsNullOrWhiteSpace(settings.RedirectUri) ? defaultRedirectUri : settings.RedirectUri;
        var state = TokenGenerator.Create();
        var nonce = TokenGenerator.Create();
        var verifier = TokenGenerator.Create();
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var expires = time.GetUtcNow().Add(StateLifetime);

        var cookie = stateProtector.Protect(new GoogleOAuthState(
            state,
            nonce,
            verifier,
            request.Intent!,
            request.IsRegistration ? request.BusinessModel : null,
            request.IsRegistration ? request.OrganizationName!.Trim() : null,
            redirectUri,
            expires));

        var query = new (string Name, string Value)[]
        {
            ("client_id", settings.ClientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", "openid email profile"),
            ("state", state),
            ("nonce", nonce),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("prompt", "select_account")
        };
        var url = $"{AuthorizeEndpoint}?{string.Join('&', query.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"))}";

        return new GoogleSignInStart(url, cookie, expires);
    }

    public async Task<GoogleSignInOutcome> CompleteAsync(
        GoogleCallback callback, string? stateCookie, CancellationToken cancellationToken)
    {
        var remembered = stateProtector.Unprotect(stateCookie);
        var intent = remembered?.Intent ?? GoogleStartRequest.Login;

        // The browser must be the one that started this sign-in: its cookie holds the same random state
        // that came back in the address. Anything else (an expired page, a callback someone else
        // crafted) is refused before Google is asked anything.
        if (remembered is null || string.IsNullOrEmpty(callback.State) || !SameText(callback.State, remembered.State))
        {
            return Failure(GoogleSignInErrors.SessionExpired, intent);
        }

        if (!string.IsNullOrEmpty(callback.Error))
        {
            return Failure(GoogleSignInErrors.Denied, intent);
        }

        if (string.IsNullOrEmpty(callback.Code))
        {
            return Failure(GoogleSignInErrors.Failed, intent);
        }

        GoogleIdentity identity;
        try
        {
            identity = await google.ExchangeCodeAsync(
                callback.Code, remembered.CodeVerifier, remembered.RedirectUri, remembered.Nonce, cancellationToken);
        }
        catch (GoogleSignInException exception)
        {
            // The message is ours and holds no code or token.
            logger.LogWarning("Google sign-in failed: {Reason}", exception.Message);
            return Failure(GoogleSignInErrors.Failed, intent);
        }

        if (!identity.EmailVerified)
        {
            return Failure(GoogleSignInErrors.EmailUnverified, intent);
        }

        var existingUserId = await accounts.FindUserIdByIdentityAsync(ExternalProviders.Google, identity.Subject, cancellationToken);
        if (existingUserId is { } userId)
        {
            return await SignInAsync(userId, intent, cancellationToken);
        }

        // Not a Google user we know. If the address already has an account the person made some other way,
        // it is not theirs to take: password sign-ups are not email-verified, so linking by address would
        // let whoever registered it first keep access.
        var email = EmailAddressHelper.Normalize(identity.Email);
        if (await accounts.FindLoginByEmailAsync(email, cancellationToken) is not null)
        {
            return Failure(GoogleSignInErrors.EmailTaken, intent);
        }

        if (intent != GoogleStartRequest.Register)
        {
            return Failure(GoogleSignInErrors.NoAccount, intent);
        }

        if (!BusinessModelRules.TryParse(remembered.BusinessModel, out var businessModel))
        {
            return Failure(GoogleSignInErrors.InvalidRequest, intent);
        }

        try
        {
            var session = await auth.RegisterExternalAsync(
                new ExternalRegistration(
                    ExternalProviders.Google,
                    identity.Subject,
                    email,
                    Truncate(identity.FullName),
                    remembered.OrganizationName,
                    businessModel),
                cancellationToken);
            return new GoogleSignInOutcome(session, null, intent);
        }
        catch (ConflictException)
        {
            // Someone registered this address, or this Google account, between the check above and now.
            return Failure(GoogleSignInErrors.EmailTaken, intent);
        }
        catch (BadRequestException)
        {
            return Failure(GoogleSignInErrors.InvalidRequest, intent);
        }
    }

    private async Task<GoogleSignInOutcome> SignInAsync(Guid userId, string intent, CancellationToken cancellationToken)
    {
        try
        {
            return new GoogleSignInOutcome(await auth.LoginExternalAsync(userId, cancellationToken), null, intent);
        }
        catch (ForbiddenException exception)
        {
            // Said only now, after Google has vouched for the person, so it tells nobody that an address has an account. The
            // two deactivations have their own words; anything else is an account that belongs to no organization.
            return Failure(
                exception.Code switch
                {
                    "account_deactivated" => GoogleSignInErrors.AccountDeactivated,
                    "organization_deactivated" => GoogleSignInErrors.OrganizationDeactivated,
                    _ => GoogleSignInErrors.NoOrganization
                },
                intent);
        }
    }

    private static GoogleSignInOutcome Failure(string code, string intent) => new(null, code, intent);

    private static string? Truncate(string? name)
    {
        var trimmed = name?.Trim();
        return trimmed is { Length: > MaxFullNameLength } ? trimmed[..MaxFullNameLength] : trimmed;
    }

    private static bool SameText(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
