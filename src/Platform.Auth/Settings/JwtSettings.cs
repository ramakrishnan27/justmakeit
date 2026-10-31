using System.Text;

namespace Platform.Auth.Settings;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs a key at least as long as its 256-bit output.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = "platform";
    public string Audience { get; set; } = "platform-web";

    /// <summary>Secret for signing access tokens. Never in the repository: user-secrets or environment only.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public byte[] GetSigningKeyBytes() => Encoding.UTF8.GetBytes(SigningKey);
}
