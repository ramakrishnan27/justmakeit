namespace Platform.Auth.Settings;

/// <summary>
/// The first site admin, created or promoted when the app migrates the database on start. Both values
/// belong in user-secrets or environment variables, never in the repository. Once the account exists
/// the password here is ignored, so it can be removed afterwards.
/// </summary>
public sealed class SiteAdminSettings
{
    public const string SectionName = "SiteAdmin";
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    /// <summary>Email of the account to make a site admin. Empty means: do nothing.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Password for the account when it has to be created. Not used for an existing account.</summary>
    public string Password { get; set; } = string.Empty;
}
