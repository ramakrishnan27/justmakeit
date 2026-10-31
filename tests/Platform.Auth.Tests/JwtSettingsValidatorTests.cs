using Platform.Auth.Settings;

namespace Platform.Auth.Tests;

public sealed class JwtSettingsValidatorTests
{
    private static readonly JwtSettingsValidator Validator = new();

    private static JwtSettings ValidSettings() => new() { SigningKey = new string('k', 32) };

    [Fact]
    public void Validate_KeyOfExactly32Bytes_Succeeds()
    {
        Assert.True(Validator.Validate(null, ValidSettings()).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingKey_FailsAndNamesTheSetting(string key)
    {
        var settings = ValidSettings();
        settings.SigningKey = key;

        var result = Validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Jwt:SigningKey is missing", result.FailureMessage);
    }

    [Fact]
    public void Validate_KeyOf31Bytes_FailsAndReportsTheLength()
    {
        var settings = ValidSettings();
        settings.SigningKey = new string('k', 31);

        var result = Validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("31 bytes", result.FailureMessage);
        Assert.Contains("at least 32", result.FailureMessage);
    }

    [Fact]
    public void Validate_CountsBytesNotCharacters()
    {
        var settings = ValidSettings();
        settings.SigningKey = new string('é', 16); // 16 characters, 32 UTF-8 bytes

        Assert.True(Validator.Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Validate_FailureMessage_NeverContainsTheKey()
    {
        var settings = ValidSettings();
        settings.SigningKey = "short-secret-value";

        var result = Validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.DoesNotContain("short-secret-value", result.FailureMessage);
    }

    [Theory]
    [InlineData("", "platform-web")]
    [InlineData("platform", "")]
    public void Validate_EmptyIssuerOrAudience_Fails(string issuer, string audience)
    {
        var settings = ValidSettings();
        settings.Issuer = issuer;
        settings.Audience = audience;

        Assert.True(Validator.Validate(null, settings).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(61)]
    public void Validate_AccessTokenLifetimeOutsideOneToSixtyMinutes_Fails(int minutes)
    {
        var settings = ValidSettings();
        settings.AccessTokenMinutes = minutes;

        Assert.True(Validator.Validate(null, settings).Failed);
    }

    [Fact]
    public void Defaults_AreFifteenMinutes()
    {
        Assert.Equal(15, new JwtSettings().AccessTokenMinutes);
    }
}
