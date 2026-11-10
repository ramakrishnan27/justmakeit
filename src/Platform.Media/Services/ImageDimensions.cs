using System.Buffers.Binary;

namespace Platform.Media.Services;

/// <summary>
/// Reads an image's width and height from the file's own header (PNG, JPEG, GIF, WebP): enough to show "1920 × 1080" and to check an
/// aspect ratio later, with no image library. It reads only what is in the first bytes it is given and never decodes a picture; a header
/// it cannot make sense of simply gives no size.
/// </summary>
public static class ImageDimensions
{
    private const int Largest = 100_000;

    public static (int Width, int Height)? TryRead(string mimeType, ReadOnlySpan<byte> bytes)
    {
        var size = mimeType switch
        {
            "image/png" => Png(bytes),
            "image/gif" => Gif(bytes),
            "image/jpeg" => Jpeg(bytes),
            "image/webp" => WebP(bytes),
            _ => null
        };

        return size is { Width: > 0 and <= Largest, Height: > 0 and <= Largest } ? size : null;
    }

    // 8 signature bytes, then the IHDR chunk: length (4), "IHDR" (4), width (4), height (4), all big-endian.
    private static (int Width, int Height)? Png(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[12..16].SequenceEqual("IHDR"u8)
            ? (BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]), BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]))
            : null;

    // "GIF89a", then the logical screen width and height as little-endian 16-bit numbers.
    private static (int Width, int Height)? Gif(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 10 ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10])) : null;

    // Walk the segments until a "start of frame" one, which holds the height and then the width.
    private static (int Width, int Height)? Jpeg(ReadOnlySpan<byte> bytes)
    {
        var i = 2;
        while (i + 4 <= bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                return null;
            }

            var marker = bytes[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            // Markers that stand alone, with no length: the start of image, a temporary one and the restarts.
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2;
                continue;
            }

            // The end of the image, or the start of its data: no size was found before it.
            if (marker is 0xD9 or 0xDA)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 2)..]);
            if (length < 2)
            {
                return null;
            }

            // The frame headers (baseline, progressive, ...), except the table and the arithmetic-coding markers that sit among them.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return i + 9 <= bytes.Length
                    ? (BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 5)..]))
                    : null;
            }

            i += 2 + length;
        }

        return null;
    }

    // "RIFF", the size, "WEBP", then a chunk that is lossy ("VP8 "), lossless ("VP8L") or extended ("VP8X").
    private static (int Width, int Height)? WebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 30 || !bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var chunk = bytes[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Frame tag (3 bytes), the start code 9D 01 2A, then 14-bit width and height.
            return bytes[23] == 0x9D && bytes[24] == 0x01 && bytes[25] == 0x2A
                ? (BinaryPrimitives.ReadUInt16LittleEndian(bytes[26..28]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..30]) & 0x3FFF)
                : null;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // The signature byte 0x2F, then width - 1 (14 bits) and height - 1 (14 bits) packed together.
            if (bytes[20] != 0x2F)
            {
                return null;
            }

            var width = 1 + (bytes[21] | ((bytes[22] & 0x3F) << 8));
            var height = 1 + ((bytes[22] >> 6) | (bytes[23] << 2) | ((bytes[24] & 0x0F) << 10));
            return (width, height);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Flags (4 bytes), then canvas width - 1 and height - 1 as 24-bit little-endian numbers.
            var width = 1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16));
            var height = 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16));
            return (width, height);
        }

        return null;
    }
}
