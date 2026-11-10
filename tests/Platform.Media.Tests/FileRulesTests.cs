using System.Buffers.Binary;
using System.Text;
using Platform.Media.Models;
using Platform.Media.Services;

namespace Platform.Media.Tests;

/// <summary>What a file is comes from its own first bytes: each allowed type, and what is refused.</summary>
public sealed class FileRulesTests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static byte[] Filler(int length = 64) => new byte[length];

    private static byte[] Riff(string form) => Cat(Ascii("RIFF"), [0x24, 0, 0, 0], Ascii(form), Filler());

    private static byte[] Ftyp(string brand) => Cat([0, 0, 0, 0x18], Ascii("ftyp"), Ascii(brand), Filler());

    public static TheoryData<byte[], string, MediaType, string> Allowed() => new()
    {
        { Cat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], Filler()), "image/png", MediaType.Image, "png" },
        { Cat([0xFF, 0xD8, 0xFF, 0xE0], Filler()), "image/jpeg", MediaType.Image, "jpg" },
        { Cat(Ascii("GIF89a"), Filler()), "image/gif", MediaType.Image, "gif" },
        { Cat(Ascii("GIF87a"), Filler()), "image/gif", MediaType.Image, "gif" },
        { Riff("WEBP"), "image/webp", MediaType.Image, "webp" },
        { Riff("WAVE"), "audio/wav", MediaType.Other, "wav" },
        { Cat(Ascii("%PDF-1.7\n"), Filler()), "application/pdf", MediaType.Document, "pdf" },
        { Cat([0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81, 0x01, 0x42, 0x82, 0x84], Ascii("webm"), Filler()), "video/webm", MediaType.Video, "webm" },
        { Ftyp("isom"), "video/mp4", MediaType.Video, "mp4" },
        { Ftyp("mp42"), "video/mp4", MediaType.Video, "mp4" },
        { Ftyp("avc1"), "video/mp4", MediaType.Video, "mp4" },
        { Ftyp("qt  "), "video/quicktime", MediaType.Video, "mov" },
        { Cat(Ascii("ID3"), [4, 0], Filler()), "audio/mpeg", MediaType.Other, "mp3" },
        { Cat([0xFF, 0xFB, 0x90, 0x64], Filler()), "audio/mpeg", MediaType.Other, "mp3" }
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void EachAllowedType_IsRecognizedFromItsFirstBytes(byte[] bytes, string mime, MediaType type, string extension)
    {
        var detected = MediaFileRules.Sniff(bytes);

        Assert.NotNull(detected);
        Assert.Equal(mime, detected.MimeType);
        Assert.Equal(type, detected.Type);
        Assert.Equal(extension, detected.Extension);
    }

    public static TheoryData<string, byte[]> Refused() => new()
    {
        { "an empty file", [] },
        { "three bytes of nothing", [0, 0, 0] },
        { "an SVG, which can carry script", Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "an HTML page", Ascii("<!doctype html><html><body>hi</body></html>") },
        { "a Windows program", Cat(Ascii("MZ"), Filler()) },
        { "a zip, which an office document also is", Cat([0x50, 0x4B, 0x03, 0x04], Filler()) },
        { "a short GIF signature", Ascii("GIF8") },
        { "a RIFF file that is neither WebP nor WAV (an AVI)", Riff("AVI ") },
        { "a Matroska video, which is not WebM", Cat([0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81, 0x01, 0x42, 0x82, 0x88], Ascii("matroska"), Filler()) },
        { "a HEIC picture, whose brand is not a video's", Ftyp("heic") },
        { "an M4A song", Ftyp("M4A ") },
        { "an MPEG sync with a reserved layer", Cat([0xFF, 0xE0, 0x90, 0x00], Filler()) },
        { "an MPEG sync with a reserved bitrate", Cat([0xFF, 0xFB, 0xF0, 0x00], Filler()) },
        { "plain text", Ascii("Hello, this is a note.") }
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void ABytePatternThatIsNotAllowed_IsRefused(string because, byte[] bytes)
    {
        Assert.True(MediaFileRules.Sniff(bytes) is null, because);
    }

    [Fact]
    public void TheExtensionsOfThePicker_AreTheTypesTheRulesKnow()
    {
        Assert.Equal(
            [".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".mov", ".webm", ".pdf", ".mp3", ".wav"],
            MediaFileRules.Extensions);
        Assert.DoesNotContain(".svg", MediaFileRules.Extensions);
    }

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("C:\\Users\\me\\Pictures\\holiday photo.jpg", "holiday photo.jpg")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("  a\tb\n.png ", "ab.png")]
    [InlineData("सूरज की रोशनी.png", "सूरज की रोशनी.png")]
    [InlineData(null, "file")]
    [InlineData("", "file")]
    [InlineData("...", "file")]
    [InlineData("/", "file")]
    [InlineData("folder/", "file")]
    public void CleanFileName_KeepsTheLastPartOfTheNameAndNeverReturnsNothing(string? raw, string expected)
    {
        Assert.Equal(expected, MediaFileRules.CleanFileName(raw));
    }

    [Fact]
    public void ALongName_IsCutToTheLimit_KeepingItsExtension()
    {
        var cleaned = MediaFileRules.CleanFileName(new string('a', 400) + ".jpeg");

        Assert.Equal(255, cleaned.Length);
        Assert.EndsWith(".jpeg", cleaned);
    }

    [Fact]
    public void ALongNameWithNoExtension_IsCutToTheLimit()
    {
        Assert.Equal(255, MediaFileRules.CleanFileName(new string('b', 400)).Length);
    }
}

public sealed class ImageDimensionsTests
{
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Be32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Be16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Le16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Cat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static byte[] Png(int width, int height) =>
        Cat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], [0, 0, 0, 13], Ascii("IHDR"), Be32(width), Be32(height), new byte[16]);

    // A JPEG: start of image, an APP0 segment of 16 bytes, then whatever segment is given.
    private static byte[] Jpeg(params byte[][] segments) =>
        Cat([0xFF, 0xD8], [0xFF, 0xE0], Be16(16), new byte[14], Cat(segments), new byte[8]);

    private static byte[] Sof(byte marker, int width, int height) =>
        Cat([0xFF, marker], Be16(17), [8], Be16(height), Be16(width), [3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);

    private static byte[] WebPHeader(string chunk) => Cat(Ascii("RIFF"), [0x40, 0, 0, 0], Ascii("WEBP"), Ascii(chunk), [0x20, 0, 0, 0]);

    [Fact]
    public void APng_HasItsSizeInTheHeaderChunk() =>
        Assert.Equal((1920, 1080), ImageDimensions.TryRead("image/png", Png(1920, 1080)));

    [Fact]
    public void AGif_HasItsSizeInTheScreenDescriptor() =>
        Assert.Equal((320, 200), ImageDimensions.TryRead("image/gif", Cat(Ascii("GIF89a"), Le16(320), Le16(200), new byte[8])));

    [Fact]
    public void ABaselineJpeg_HasItsSizeInTheFrameHeader() =>
        Assert.Equal((4000, 3000), ImageDimensions.TryRead("image/jpeg", Jpeg(Sof(0xC0, 4000, 3000))));

    [Fact]
    public void AProgressiveJpeg_IsReadToo() =>
        Assert.Equal((800, 600), ImageDimensions.TryRead("image/jpeg", Jpeg(Sof(0xC2, 800, 600))));

    [Fact]
    public void AJpeg_WithATableBeforeTheFrameHeader_IsStillRead()
    {
        // A "define Huffman table" segment (0xC4) looks like a frame header by its number but is not one.
        var table = Cat([0xFF, 0xC4], Be16(6), [0, 0, 0, 0]);

        Assert.Equal((640, 480), ImageDimensions.TryRead("image/jpeg", Jpeg(table, Sof(0xC0, 640, 480))));
    }

    [Fact]
    public void AJpeg_WhoseDataStartsBeforeAnySize_HasNone()
    {
        var startOfScan = Cat([0xFF, 0xDA], Be16(4), [0, 0]);

        Assert.Null(ImageDimensions.TryRead("image/jpeg", Jpeg(startOfScan, Sof(0xC0, 640, 480))));
    }

    [Fact]
    public void ALossyWebP_HasItsSizeAfterTheStartCode()
    {
        var bytes = Cat(WebPHeader("VP8 "), [0x30, 0x01, 0x00], [0x9D, 0x01, 0x2A], Le16(640), Le16(360), new byte[8]);

        Assert.Equal((640, 360), ImageDimensions.TryRead("image/webp", bytes));
    }

    [Fact]
    public void ALosslessWebP_HasItsSizePackedInTheFirstBits()
    {
        // Width 100 and height 50, each stored minus one in 14 bits: 99 = 0x63 and 49 = 0b110001.
        var bytes = Cat(WebPHeader("VP8L"), [0x2F, 0x63, 0x40, 0x0C, 0x00], new byte[8]);

        Assert.Equal((100, 50), ImageDimensions.TryRead("image/webp", bytes));
    }

    [Fact]
    public void AnExtendedWebP_HasItsCanvasSizeInThreeBytesEach()
    {
        // 1280 x 720 stored minus one as 24-bit little-endian: 1279 = FF 04 00 and 719 = CF 02 00.
        var bytes = Cat(WebPHeader("VP8X"), [0, 0, 0, 0], [0xFF, 0x04, 0x00], [0xCF, 0x02, 0x00], new byte[4]);

        Assert.Equal((1280, 720), ImageDimensions.TryRead("image/webp", bytes));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void ATruncatedFile_HasNoSize_AndDoesNotThrow(string mime)
    {
        Assert.Null(ImageDimensions.TryRead(mime, new byte[] { 0x89, 0x50 }));
        Assert.Null(ImageDimensions.TryRead(mime, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void ASizeOfZeroOrAbsurdSize_IsNoSize()
    {
        Assert.Null(ImageDimensions.TryRead("image/png", Png(0, 100)));
        Assert.Null(ImageDimensions.TryRead("image/png", Png(100, 0)));
        Assert.Null(ImageDimensions.TryRead("image/png", Png(200_000, 100)));
        Assert.Null(ImageDimensions.TryRead("image/png", Png(-5, 100)));
    }

    [Fact]
    public void ATypeThatIsNotAnImage_HasNoSize() =>
        Assert.Null(ImageDimensions.TryRead("application/pdf", Png(10, 10)));

    [Fact]
    public void AJpegWithGarbageSegmentLengths_DoesNotLoopOrThrow()
    {
        // A segment whose length says 0 or 1 is not possible (the length counts its own two bytes).
        var bad = Cat([0xFF, 0xD8, 0xFF, 0xE0], Be16(0), new byte[16]);

        Assert.Null(ImageDimensions.TryRead("image/jpeg", bad));
    }
}
