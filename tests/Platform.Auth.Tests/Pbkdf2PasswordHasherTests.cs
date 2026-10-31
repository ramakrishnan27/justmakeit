using System.Security.Cryptography;
using Platform.Auth.Services;

namespace Platform.Auth.Tests;

public sealed class Pbkdf2PasswordHasherTests
{
    // Few iterations keep the tests quick; the default of 600,000 is checked on its own below.
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    [Fact]
    public void Hash_HasTheDocumentedShape()
    {
        var parts = _hasher.Hash("correct horse battery staple").Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_UsesSixHundredThousandIterationsByDefault()
    {
        var hash = new Pbkdf2PasswordHasher().Hash("anything at all");

        Assert.StartsWith("pbkdf2$600000$", hash);
        Assert.Equal(600_000, Pbkdf2PasswordHasher.DefaultIterations);
    }

    [Fact]
    public void Hash_DoesNotContainThePassword()
    {
        Assert.DoesNotContain("hunter2-hunter2", _hasher.Hash("hunter2-hunter2"));
    }

    [Fact]
    public void Hash_OfTheSamePasswordTwice_DiffersBecauseOfTheSalt()
    {
        Assert.NotEqual(_hasher.Hash("same password"), _hasher.Hash("same password"));
    }

    [Fact]
    public void Verify_AcceptsTheRightPassword()
    {
        Assert.True(_hasher.Verify("right password", _hasher.Hash("right password")));
    }

    [Theory]
    [InlineData("wrong password")]
    [InlineData("Right password")]
    [InlineData("right password ")]
    [InlineData("")]
    public void Verify_RejectsAnyOtherPassword(string attempt)
    {
        Assert.False(_hasher.Verify(attempt, _hasher.Hash("right password")));
    }

    [Fact]
    public void Verify_ReadsTheIterationCountFromTheStoredHash()
    {
        // A hash made with another cost still verifies, so raising the default never locks users out.
        var stored = new Pbkdf2PasswordHasher(iterations: 5).Hash("old password");

        Assert.True(_hasher.Verify("old password", stored));
    }

    [Fact]
    public void Verify_MatchesTheStandardPbkdf2Sha256Result()
    {
        // PBKDF2-HMAC-SHA256, password "password", salt "salt", 1 iteration, 32 bytes.
        var expected = Convert.FromHexString("120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b");
        var stored = $"pbkdf2$1${Convert.ToBase64String("salt"u8.ToArray())}${Convert.ToBase64String(expected)}";

        Assert.True(_hasher.Verify("password", stored));
    }

    [Fact]
    public void Verify_ComparesInConstantTime_ThroughFixedTimeEquals()
    {
        // Not measurable in a unit test; this pins the primitive the implementation relies on.
        Assert.True(CryptographicOperations.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
        Assert.False(CryptographicOperations.FixedTimeEquals(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2$1000$c2FsdA==")]
    [InlineData("pbkdf2$1000$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==$extra")]
    [InlineData("bcrypt$1000$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$abc$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$0$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$-5$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$99999999999$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$1000$not base64!$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$1000$c2FsdA==$not base64!")]
    [InlineData("pbkdf2$1000$$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("pbkdf2$1000$c2FsdA==$AAAA")]
    public void Verify_NeverMatchesAMalformedStoredHash(string stored)
    {
        Assert.False(_hasher.Verify("whatever", stored));
    }

    [Fact]
    public void Verify_RefusesAnAbsurdIterationCount_WithoutComputingIt()
    {
        var stored = $"pbkdf2$2000000000${Convert.ToBase64String(new byte[16])}${Convert.ToBase64String(new byte[32])}";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(_hasher.Verify("whatever", stored));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DummyHash_IsAWellFormedHashNobodyCanMatch()
    {
        var parts = _hasher.DummyHash.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.False(_hasher.Verify("password", _hasher.DummyHash));
        Assert.False(_hasher.Verify("", _hasher.DummyHash));
    }

    [Fact]
    public void DummyHash_CostsTheSameAsARealHash()
    {
        Assert.Equal(_hasher.Hash("x").Split('$')[1], _hasher.DummyHash.Split('$')[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsANonPositiveIterationCount(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pbkdf2PasswordHasher(iterations));
    }

    [Fact]
    public void HashAndVerify_RejectANullPassword()
    {
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(null!));
        Assert.Throws<ArgumentNullException>(() => _hasher.Verify(null!, _hasher.Hash("x")));
    }
}
