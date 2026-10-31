using Platform.Helpers.Utilities;

namespace Platform.Helpers.Tests;

public sealed class TrackingPixelTests
{
    [Fact]
    public void Gif_IsA43ByteGif89aImage()
    {
        var gif = TrackingPixel.Gif.ToArray();

        Assert.Equal(43, gif.Length);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(gif, 0, 6));
        Assert.Equal(0x3B, gif[^1]); // GIF trailer
    }

    [Fact]
    public void Gif_Declares1x1Dimensions()
    {
        var gif = TrackingPixel.Gif.ToArray();

        Assert.Equal(1, gif[6] | (gif[7] << 8));
        Assert.Equal(1, gif[8] | (gif[9] << 8));
    }

    [Fact]
    public void ContentType_IsImageGif()
    {
        Assert.Equal("image/gif", TrackingPixel.ContentType);
    }
}
