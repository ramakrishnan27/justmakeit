using System.Globalization;
using System.Security.Cryptography;
using Platform.Auth.Interfaces;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Services;

/// <summary>
/// PBKDF2 with SHA-256, a random 16-byte salt and, by default, 600,000 iterations, stored as
/// <c>pbkdf2$iterations$salt$hash</c> (salt and hash in base64). The iteration count is part of the
/// stored value, so raising the default later does not lock out existing users.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 600_000;

    private const string Scheme = "pbkdf2";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // A tampered or corrupt stored value must not be able to make a login spin the CPU for minutes.
    private const int MaxAcceptedIterations = 10_000_000;
    private const int MinAcceptedHashBytes = 16;
    private const int MaxAcceptedHashBytes = 64;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxAcceptedIterations);
        _iterations = iterations;

        // Computed once, with the same cost as a real hash, so an unknown email takes as long as a wrong password.
        DummyHash = Hash(TokenGenerator.Create());
    }

    public string DummyHash { get; }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Scheme, _iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(storedHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParse(string? stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        var parts = stored?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != Scheme)
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations is < 1 or > MaxAcceptedIterations)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && hash.Length is >= MinAcceptedHashBytes and <= MaxAcceptedHashBytes;
    }
}
