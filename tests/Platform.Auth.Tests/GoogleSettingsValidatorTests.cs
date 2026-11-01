using Platform.Auth.Settings;

namespace Platform.Auth.Tests;

public sealed class GoogleSettingsValidatorTests
{
    private static bool Passes(GoogleSettings settings, out string? failure)
    {
        var result = new GoogleSettingsValidator().Validate(null, settings);
        failure = result.FailureMessage;
        return result.Succeeded;
    }

    [Fact]
    public void WithNothingSet_GoogleIsOff_AndTheAppStillStarts()
    {
        var settings = new GoogleSettings();

        Assert.True(Passes(settings, out _));
        Assert.False(settings.Enabled);
    }

    [Fact]
    public void WithAnIdAndASecret_GoogleIsOn()
    {
        var settings = new GoogleSettings { ClientId = "id.apps.googleusercontent.com", ClientSecret = "secret" };

        Assert.True(Passes(settings, out _));
        Assert.True(settings.Enabled);
    }

    [Theory]
    [InlineData("id.apps.googleusercontent.com", "")]
    [InlineData("", "secret")]
    [InlineData("   ", "secret")]
    [InlineData("id.apps.googleusercontent.com", "   ")]
    public void WithOnlyOneOfTheTwo_TheAppRefusesToStart_RatherThanQuietlyLeavingGoogleOff(string id, string secret)
    {
        var settings = new GoogleSettings { ClientId = id, ClientSecret = secret };

        Assert.False(Passes(settings, out var failure));
        Assert.Contains("go together", failure);
        Assert.False(settings.Enabled);
    }

    [Theory]
    [InlineData("https://app.example.com/v1/auth/google/callback")]
    [InlineData("http://localhost:5080/v1/auth/google/callback")]
    [InlineData("http://127.0.0.1:5080/v1/auth/google/callback")]
    [InlineData("")]
    public void ARedirectAddress_MustBeHttps_OrLocalhost(string redirectUri)
    {
        var settings = new GoogleSettings { ClientId = "id", ClientSecret = "secret", RedirectUri = redirectUri };

        Assert.True(Passes(settings, out _));
    }

    [Theory]
    [InlineData("http://app.example.com/v1/auth/google/callback")]
    [InlineData("/v1/auth/google/callback")]
    [InlineData("ftp://app.example.com/callback")]
    [InlineData("not a url")]
    public void ARedirectAddressThatIsPlainHttpElsewhere_OrNotAbsolute_IsRefused(string redirectUri)
    {
        var settings = new GoogleSettings { ClientId = "id", ClientSecret = "secret", RedirectUri = redirectUri };

        Assert.False(Passes(settings, out var failure));
        Assert.Contains("Google:RedirectUri", failure);
    }
}
