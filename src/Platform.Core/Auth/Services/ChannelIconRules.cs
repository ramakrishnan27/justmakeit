namespace Platform.Auth.Services;

/// <summary>
/// What may be uploaded as a channel's icon: a PNG, JPEG or WebP image of at most 100 KB. The type is read from the file's
/// own first bytes, never taken from what the browser claims, and a file that says it is one thing and is another is
/// refused. SVG and GIF are left out on purpose (an SVG can carry script). The database CHECKs repeat the size and the types.
/// </summary>
public static class ChannelIconRules
{
    public const int MaxBytes = 100 * 1024;

    /// <summary>The built-in glyph a channel gets when no image is uploaded. The web client draws it (a test keeps the two in step).</summary>
    public const string DefaultGlyph = "broadcast";

    private const string Unsupported = "The icon must be a PNG, JPEG or WebP image.";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The type of an image from its first bytes, or null when it is not one of the three allowed.</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>
    /// What is wrong with an uploaded icon, or null when it can be stored. <paramref name="contentType"/> is the type found in
    /// the file, which is the one to store.
    /// </summary>
    public static string? Problem(byte[] image, string? declaredContentType, out string contentType)
    {
        contentType = string.Empty;

        if (image.Length == 0)
        {
            return "The icon file is empty.";
        }

        if (image.Length > MaxBytes)
        {
            return $"The icon must be at most {MaxBytes / 1024} KB.";
        }

        var found = SniffContentType(image);
        if (found is null)
        {
            return Unsupported;
        }

        // A browser names the type of the file it sends. A different claim means the file is not what it says it is.
        if (!string.IsNullOrWhiteSpace(declaredContentType)
            && !string.Equals(declaredContentType.Trim(), found, StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported;
        }

        contentType = found;
        return null;
    }
}
