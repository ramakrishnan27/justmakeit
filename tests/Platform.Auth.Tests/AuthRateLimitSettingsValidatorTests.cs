using Platform.Auth.Settings;

namespace Platform.Auth.Tests;

public sealed class AuthRateLimitSettingsValidatorTests
{
    private static readonly AuthRateLimitSettingsValidator Validator = new();

    [Fact]
    public void Defaults_AreValid_AndTighterForCredentialsThanForTheSession()
    {
        var defaults = new AuthRateLimitSettings();

        Assert.True(Validator.Validate(null, defaults).Succeeded);
        Assert.Equal(10, defaults.CredentialRequestsPerWindow);
        Assert.Equal(60, defaults.SessionRequestsPerWindow);
        Assert.Equal(60, defaults.WindowSeconds);
        Assert.True(defaults.CredentialRequestsPerWindow < defaults.SessionRequestsPerWindow);
    }

    [Theory]
    [InlineData(0, 60, 60)]
    [InlineData(10, 0, 60)]
    [InlineData(-1, 60, 60)]
    [InlineData(10, 60, 0)]
    [InlineData(10, 60, -30)]
    public void Validate_ALimitOrWindowBelowOne_FailsAndNamesTheSection(int credentials, int session, int window)
    {
        var settings = new AuthRateLimitSettings
        {
            CredentialRequestsPerWindow = credentials,
            SessionRequestsPerWindow = session,
            WindowSeconds = window
        };

        var result = Validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("RateLimiting:Auth", result.FailureMessage);
    }
}
