using System.Security.Cryptography;

namespace Platform.Helpers.Utilities;

public static class TokenGenerator
{
    public const int DefaultByteLength = 32;
    private const int MinimumByteLength = 16;

    /// <summary>Random token from a cryptographic generator, encoded as unpadded base64url.</summary>
    public static string Create(int byteLength = DefaultByteLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteLength, MinimumByteLength);

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
