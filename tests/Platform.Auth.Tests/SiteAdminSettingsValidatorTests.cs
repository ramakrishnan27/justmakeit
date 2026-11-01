using Platform.Auth.Settings;

namespace Platform.Auth.Tests;

public sealed class SiteAdminSettingsValidatorTests
{
    private static readonly SiteAdminSettingsValidator Validator = new();

    [Fact]
    public void Validate_WithNothingConfigured_Succeeds_BecauseASiteAdminIsOptional()
    {
        Assert.True(Validator.Validate(null, new SiteAdminSettings()).Succeeded);
    }

    [Fact]
    public void Validate_WithAnEmailAndAPassword_Succeeds()
    {
        var settings = new SiteAdminSettings { Email = "boss@example.com", Password = new string('p', 12) };

        Assert.True(Validator.Validate(null, settings).Succeeded);
    }

    [Fact]
    public void Validate_WithOnlyAnEmail_Succeeds_SoAnExistingAccountCanBePromoted()
    {
        Assert.True(Validator.Validate(null, new SiteAdminSettings { Email = "boss@example.com" }).Succeeded);
    }

    [Fact]
    public void Validate_WithAPasswordButNoEmail_FailsAndNamesBothSettings()
    {
        var result = Validator.Validate(null, new SiteAdminSettings { Password = new string('p', 12) });

        Assert.True(result.Failed);
        Assert.Contains("SiteAdmin:Password", result.FailureMessage);
        Assert.Contains("SiteAdmin:Email", result.FailureMessage);
    }

    [Theory]
    [InlineData("not an email")]
    [InlineData("missing-domain@")]
    [InlineData("two@@example.com")]
    public void Validate_WithABadEmail_Fails(string email)
    {
        var result = Validator.Validate(null, new SiteAdminSettings { Email = email });

        Assert.True(result.Failed);
        Assert.Contains("SiteAdmin:Email", result.FailureMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(129)]
    public void Validate_WithAPasswordOutsideTwelveTo128Characters_Fails(int length)
    {
        var result = Validator.Validate(null, new SiteAdminSettings { Email = "boss@example.com", Password = new string('p', length) });

        Assert.True(result.Failed);
        Assert.Contains("SiteAdmin:Password", result.FailureMessage);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public void Validate_WithAPasswordAtTheLimits_Succeeds(int length)
    {
        Assert.True(Validator.Validate(null, new SiteAdminSettings { Email = "boss@example.com", Password = new string('p', length) }).Succeeded);
    }
}
