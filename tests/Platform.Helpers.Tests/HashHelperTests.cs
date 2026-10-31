using Platform.Helpers.Utilities;

namespace Platform.Helpers.Tests;

public sealed class HashHelperTests
{
    [Fact]
    public void Sha256Hex_MatchesKnownVector()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", HashHelper.Sha256Hex("abc"));
    }

    [Fact]
    public void Sha256Hex_OfEmptyString_MatchesKnownVector()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", HashHelper.Sha256Hex(string.Empty));
    }

    [Fact]
    public void Sha256Hex_IsLowerCaseHexOfFixedLength()
    {
        var hash = HashHelper.Sha256Hex("anything");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Sha256Hex_HashesUtf8Bytes()
    {
        Assert.Equal(HashHelper.Sha256Hex("café"u8), HashHelper.Sha256Hex("café"));
    }

    [Fact]
    public void Sha256Hex_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HashHelper.Sha256Hex((string)null!));
    }
}
