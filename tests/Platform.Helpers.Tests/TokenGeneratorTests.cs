using Platform.Helpers.Utilities;

namespace Platform.Helpers.Tests;

public sealed class TokenGeneratorTests
{
    [Fact]
    public void Create_DefaultLength_Is43UrlSafeCharacters()
    {
        var token = TokenGenerator.Create();

        Assert.Equal(43, token.Length); // 32 bytes, unpadded base64url
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Theory]
    [InlineData(16, 22)]
    [InlineData(24, 32)]
    [InlineData(48, 64)]
    public void Create_HonoursByteLength(int bytes, int expectedChars)
    {
        Assert.Equal(expectedChars, TokenGenerator.Create(bytes).Length);
    }

    [Fact]
    public void Create_ReturnsDifferentTokensEachTime()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => TokenGenerator.Create()).ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }

    [Fact]
    public void Create_BelowMinimumLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenGenerator.Create(15));
    }
}
