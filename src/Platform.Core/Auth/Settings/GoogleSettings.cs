namespace Platform.Auth.Settings;

/// <summary>
/// "Continue with Google": an OAuth client made in Google Cloud Console. The client secret belongs in
/// user-secrets or an environment variable, never in the repository. With no client id and secret the
/// feature is off: the pages do not offer the button and the endpoints refuse.
/// </summary>
public sealed class GoogleSettings
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The address Google sends the person back to, exactly as registered in Google Cloud Console.
    /// Empty means <c>{scheme}://{host}/v1/auth/google/callback</c> of the request that started the sign-in.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
