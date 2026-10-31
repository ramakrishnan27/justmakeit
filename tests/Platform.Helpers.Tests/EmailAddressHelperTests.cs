using Platform.Helpers.Utilities;

namespace Platform.Helpers.Tests;

public sealed class EmailAddressHelperTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@example.com")]
    [InlineData("user+tag@example.co.uk")]
    [InlineData("o'brien@example.com")]
    [InlineData("a_b-c@sub.example.org")]
    [InlineData("1234567890@example.com")]
    [InlineData("user@xn--bcher-kva.example")]
    [InlineData("user@example.xn--p1ai")]
    public void IsValid_AcceptsOrdinaryAddresses(string email)
    {
        Assert.True(EmailAddressHelper.IsValid(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("a@b@example.com")]
    [InlineData("user@localhost")]
    [InlineData("user@example")]
    [InlineData("user@example.c")]
    [InlineData("user@example.123")]
    [InlineData("user@[192.168.0.1]")]
    [InlineData("user@-example.com")]
    [InlineData("user@example-.com")]
    [InlineData("user@exa mple.com")]
    [InlineData("us er@example.com")]
    [InlineData(".user@example.com")]
    [InlineData("user.@example.com")]
    [InlineData("us..er@example.com")]
    [InlineData("\"quoted\"@example.com")]
    [InlineData("User <user@example.com>")]
    [InlineData("user@example.com, other@example.com")]
    [InlineData("user(comment)@example.com")]
    [InlineData("üser@example.com")]
    [InlineData("user@exämple.com")]
    public void IsValid_RejectsMalformedAddresses(string? email)
    {
        Assert.False(EmailAddressHelper.IsValid(email));
    }

    [Fact]
    public void IsValid_RejectsLocalPartLongerThan64()
    {
        Assert.True(EmailAddressHelper.IsValid(new string('a', 64) + "@example.com"));
        Assert.False(EmailAddressHelper.IsValid(new string('a', 65) + "@example.com"));
    }

    [Fact]
    public void IsValid_AcceptsExactly254CharactersAndRejects255()
    {
        // "a@" + three 63-character labels + one variable label + ".com"
        static string Address(int lastLabelLength) =>
            $"a@{new string('d', 63)}.{new string('d', 63)}.{new string('d', 63)}.{new string('d', lastLabelLength)}.com";

        var longest = Address(56);
        var tooLong = Address(57);

        Assert.Equal(EmailAddressHelper.MaxLength, longest.Length);
        Assert.True(EmailAddressHelper.IsValid(longest));
        Assert.Equal(EmailAddressHelper.MaxLength + 1, tooLong.Length);
        Assert.False(EmailAddressHelper.IsValid(tooLong));
    }

    [Fact]
    public void IsValid_RejectsDomainLabelLongerThan63()
    {
        Assert.True(EmailAddressHelper.IsValid($"user@{new string('a', 63)}.com"));
        Assert.False(EmailAddressHelper.IsValid($"user@{new string('a', 64)}.com"));
    }

    [Theory]
    [InlineData("  User@Example.COM ", "user@example.com")]
    [InlineData("USER@EXAMPLE.COM", "user@example.com")]
    [InlineData("user@example.com", "user@example.com")]
    public void TryNormalize_TrimsAndLowerCases(string input, string expected)
    {
        Assert.True(EmailAddressHelper.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalize_InvalidAddress_ReturnsFalseAndNull()
    {
        Assert.False(EmailAddressHelper.TryNormalize("not-an-email", out var normalized));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  Mixed@Case.com ", "mixed@case.com")]
    public void Normalize_NeverThrows_AndDoesNotValidate(string? input, string expected)
    {
        Assert.Equal(expected, EmailAddressHelper.Normalize(input));
    }

    [Fact]
    public void GetDomain_ReturnsPartAfterTheAt()
    {
        Assert.Equal("example.co.uk", EmailAddressHelper.GetDomain("user@example.co.uk"));
    }
}
