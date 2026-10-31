using System.Security.Cryptography;
using System.Text;

namespace Platform.Helpers.Utilities;

public static class HashHelper
{
    /// <summary>SHA-256 of the UTF-8 bytes of <paramref name="value"/>, as lower-case hex.</summary>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256Hex(Encoding.UTF8.GetBytes(value));
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
