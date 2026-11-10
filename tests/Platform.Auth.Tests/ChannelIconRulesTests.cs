using Platform.Auth.Services;

namespace Platform.Auth.Tests;

public sealed class ChannelIconRulesTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

    /// <summary>A file that starts with the given bytes and is padded to the given length.</summary>
    private static byte[] File(byte[] start, int length = 64)
    {
        var bytes = new byte[Math.Max(length, start.Length)];
        start.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] WebP(int length = 64)
    {
        var bytes = new byte[length];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    [Fact]
    public void ThePngJpegAndWebPSignatures_AreRecognizedFromTheBytesAlone()
    {
        Assert.Equal("image/png", ChannelIconRules.SniffContentType(File(PngHeader)));
        Assert.Equal("image/jpeg", ChannelIconRules.SniffContentType(File(JpegHeader)));
        Assert.Equal("image/webp", ChannelIconRules.SniffContentType(WebP()));
    }

    [Theory]
    [InlineData("GIF89a......")]
    [InlineData("GIF87a......")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("<?xml version=\"1.0\"?><svg></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("plain text that is not an image at all")]
    [InlineData("BM......")]
    public void AnythingElse_IsNotAnImage_WhateverItIsNamed(string text)
    {
        Assert.Null(ChannelIconRules.SniffContentType(System.Text.Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void ARiffFileThatIsNotWebP_IsNotAnImage()
    {
        var wav = new byte[64];
        "RIFF"u8.CopyTo(wav);
        "WAVE"u8.CopyTo(wav.AsSpan(8));

        Assert.Null(ChannelIconRules.SniffContentType(wav));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void ATruncatedSignature_IsNotAnImage(int length)
    {
        Assert.Null(ChannelIconRules.SniffContentType(PngHeader.AsSpan(0, Math.Min(length, PngHeader.Length))));
    }

    [Fact]
    public void AnAcceptableFile_HasNoProblem_AndTheStoredTypeIsTheOneFoundInTheFile()
    {
        Assert.Null(ChannelIconRules.Problem(File(PngHeader), "image/png", out var png));
        Assert.Null(ChannelIconRules.Problem(File(JpegHeader), "image/jpeg", out var jpeg));
        Assert.Null(ChannelIconRules.Problem(WebP(), "image/webp", out var webp));

        Assert.Equal(["image/png", "image/jpeg", "image/webp"], [png, jpeg, webp]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("IMAGE/PNG")]
    [InlineData(" image/png ")]
    public void TheDeclaredType_MayBeMissing_OrDifferInLetterCase(string? declared)
    {
        Assert.Null(ChannelIconRules.Problem(File(PngHeader), declared, out var type));
        Assert.Equal("image/png", type);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    public void AFileThatSaysItIsAnotherType_IsRefused(string declared)
    {
        var problem = ChannelIconRules.Problem(File(PngHeader), declared, out var type);

        Assert.Equal("The icon must be a PNG, JPEG or WebP image.", problem);
        Assert.Equal(string.Empty, type);
    }

    [Fact]
    public void ASvgThatSaysItIsAPng_IsRefused()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>");

        Assert.Equal("The icon must be a PNG, JPEG or WebP image.", ChannelIconRules.Problem(svg, "image/png", out _));
    }

    [Fact]
    public void AnEmptyFile_IsRefused()
    {
        Assert.Equal("The icon file is empty.", ChannelIconRules.Problem([], "image/png", out _));
    }

    [Fact]
    public void TheLimitIs100KB_AndTheLastAcceptedByteIsTheLimit()
    {
        Assert.Equal(102_400, ChannelIconRules.MaxBytes);
        Assert.Null(ChannelIconRules.Problem(File(PngHeader, ChannelIconRules.MaxBytes), "image/png", out _));
        Assert.Equal(
            "The icon must be at most 100 KB.",
            ChannelIconRules.Problem(File(PngHeader, ChannelIconRules.MaxBytes + 1), "image/png", out _));
    }
}
