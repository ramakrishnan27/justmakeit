namespace Platform.Auth.Settings;

/// <summary>
/// Request limits per client address on the /v1/auth/* endpoints, applied in fixed windows.
/// Credentials endpoints (register, login) are the ones worth guessing against, so they get the tighter limit.
/// </summary>
public sealed class AuthRateLimitSettings
{
    public const string SectionName = "RateLimiting:Auth";

    /// <summary>Register and login requests allowed per window and address.</summary>
    public int CredentialRequestsPerWindow { get; set; } = 10;

    /// <summary>Logout, switch-agency, me and the other session requests allowed per window and address.</summary>
    public int SessionRequestsPerWindow { get; set; } = 60;

    public int WindowSeconds { get; set; } = 60;
}
