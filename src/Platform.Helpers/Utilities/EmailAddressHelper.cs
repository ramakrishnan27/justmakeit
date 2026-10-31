using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Platform.Helpers.Utilities;

/// <summary>
/// Normalizes and validates plain addresses of the form <c>local@domain.tld</c>. Display names,
/// quoted local parts, comments, IP-literal domains and non-ASCII addresses are rejected.
/// </summary>
public static partial class EmailAddressHelper
{
    public const int MaxLength = 254;
    private const int MaxLocalPartLength = 64;

    /// <summary>Trims and lower-cases. Does not validate.</summary>
    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValid(string? email) => TryNormalize(email, out _);

    /// <summary>Normalizes <paramref name="email"/> and reports whether the result is a valid address.</summary>
    public static bool TryNormalize(string? email, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        var candidate = Normalize(email);

        if (candidate.Length is 0 or > MaxLength)
        {
            return false;
        }

        var at = candidate.IndexOf('@');
        if (at <= 0 || at != candidate.LastIndexOf('@') || at == candidate.Length - 1)
        {
            return false;
        }

        var local = candidate[..at];
        var domain = candidate[(at + 1)..];

        if (local.Length > MaxLocalPartLength || !LocalPartPattern().IsMatch(local) || !DomainPattern().IsMatch(domain))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>Domain part of an address that already passed <see cref="TryNormalize"/>.</summary>
    public static string GetDomain(string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        return normalizedEmail[(normalizedEmail.LastIndexOf('@') + 1)..];
    }

    [GeneratedRegex(@"^[a-z0-9!#$%&'*+/=?^_`{|}~-]+(\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPartPattern();

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();
}
