using System.Text;
using Platform.Media.Models;

namespace Platform.Media.Services;

/// <summary>What was found out about a file from its own first bytes: its real type, the group it belongs to and the extension to store it under.</summary>
public sealed record DetectedFile(string MimeType, MediaType Type, string Extension);

/// <summary>
/// What may be uploaded, and how a file is recognized. The type is read from the file's own first bytes, never taken from the name
/// or from what the browser claims, because both are chosen by the sender. The list is short on purpose: SVG is left out (it can
/// carry script), and so are the formats a browser cannot show and the office documents (a zip archive that only looks like one).
/// </summary>
public static class MediaFileRules
{
    /// <summary>How much of the start of a file is read to recognize it and to find an image's size. A JPEG may keep its size after a large thumbnail.</summary>
    public const int HeaderBytes = 256 * 1024;

    /// <summary>What a person may choose, for the file picker's <c>accept</c> and the message under it.</summary>
    public static IReadOnlyList<string> Extensions { get; } =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".mov", ".webm", ".pdf", ".mp3", ".wav"];

    public const string AcceptedDescription = "PNG, JPEG, GIF, WebP, MP4, MOV, WebM, PDF, MP3 or WAV";

    // The "ftyp" brands of an MP4 file; "qt  " is a QuickTime movie. Anything else (a HEIC picture, an M4A song) is not one of ours.
    private static readonly HashSet<string> Mp4Brands = new(StringComparer.Ordinal)
    {
        "isom", "iso2", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "dash", "M4V ", "MSNV", "mmp4"
    };

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The type of a file from its first bytes, or null when it is not one of the allowed ones.</summary>
    public static DetectedFile? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(PngSignature))
        {
            return new DetectedFile("image/png", MediaType.Image, "png");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return new DetectedFile("image/jpeg", MediaType.Image, "jpg");
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return new DetectedFile("image/gif", MediaType.Image, "gif");
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8))
        {
            var form = bytes[8..12];
            if (form.SequenceEqual("WEBP"u8))
            {
                return new DetectedFile("image/webp", MediaType.Image, "webp");
            }

            if (form.SequenceEqual("WAVE"u8))
            {
                return new DetectedFile("audio/wav", MediaType.Other, "wav");
            }

            return null;
        }

        if (bytes.StartsWith("%PDF-"u8))
        {
            return new DetectedFile("application/pdf", MediaType.Document, "pdf");
        }

        // The container of WebM is the one of Matroska, whose document type says which: "webm" appears in the first few bytes.
        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
        {
            return bytes[..Math.Min(bytes.Length, 64)].IndexOf("webm"u8) >= 0
                ? new DetectedFile("video/webm", MediaType.Video, "webm")
                : null;
        }

        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(bytes[8..12]);
            if (brand == "qt  ")
            {
                return new DetectedFile("video/quicktime", MediaType.Video, "mov");
            }

            return Mp4Brands.Contains(brand) ? new DetectedFile("video/mp4", MediaType.Video, "mp4") : null;
        }

        if (bytes.StartsWith("ID3"u8) || IsMp3Frame(bytes))
        {
            return new DetectedFile("audio/mpeg", MediaType.Other, "mp3");
        }

        return null;
    }

    // An MPEG audio frame header: eleven bits of sync, then a version and a layer that are not the reserved values.
    private static bool IsMp3Frame(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0 && ((bytes[1] >> 3) & 0x3) != 1 && ((bytes[1] >> 1) & 0x3) != 0
        && (bytes[2] >> 4) != 0xF;

    /// <summary>
    /// The name to keep and show: only the last part (a path someone sent is cut to its file name), without control characters, at most
    /// 255 characters with the extension kept, and never empty. It is for showing; it is never used to find the file on disk.
    /// </summary>
    public static string CleanFileName(string? raw)
    {
        var name = (raw ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();

        if (name.Length == 0 || name.All(character => character == '.'))
        {
            return "file";
        }

        const int max = 255;
        if (name.Length <= max)
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var extension = dot > 0 && name.Length - dot <= 16 ? name[dot..] : string.Empty;
        return name[..(max - extension.Length)] + extension;
    }
}
