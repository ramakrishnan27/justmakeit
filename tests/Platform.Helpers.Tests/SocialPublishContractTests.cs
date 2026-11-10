using Platform.Helpers.Publishing;

namespace Platform.Helpers.Tests;

/// <summary>The publisher contract of Core: what a channel hands back to the content feature.</summary>
public sealed class SocialPublishContractTests
{
    [Fact]
    public void Success_CarriesTheExternalIdAndAddress_AndNoError()
    {
        var result = SocialPublishResult.Success("12345", "https://example.com/p/12345");

        Assert.True(result.Succeeded);
        Assert.Equal("12345", result.ExternalPostId);
        Assert.Equal("https://example.com/p/12345", result.ExternalPostUrl);
        Assert.Null(result.Error);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void Success_NeedsAnExternalId_ButNotAnAddress()
    {
        Assert.Null(SocialPublishResult.Success("12345").ExternalPostUrl);
        Assert.Throws<ArgumentException>(() => SocialPublishResult.Success(" "));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failure_CarriesTheReasonAndWhetherToTryAgain_AndNoExternalPost(bool retryable)
    {
        var result = SocialPublishResult.Failure("The platform is busy.", retryable);

        Assert.False(result.Succeeded);
        Assert.Equal("The platform is busy.", result.Error);
        Assert.Equal(retryable, result.Retryable);
        Assert.Null(result.ExternalPostId);
        Assert.Null(result.ExternalPostUrl);
    }

    [Fact]
    public void Failure_IsNotRetryableUnlessSaidSo_AndNeedsAReason()
    {
        Assert.False(SocialPublishResult.Failure("No account is connected.").Retryable);
        Assert.Throws<ArgumentException>(() => SocialPublishResult.Failure(""));
    }

    [Fact]
    public void ARequest_DefaultsToNoHashtagsAndNoSettings()
    {
        var request = new SocialPublishRequest { PublicationId = Guid.NewGuid(), AgencyId = Guid.NewGuid(), Text = "Hello" };

        Assert.Empty(request.Hashtags);
        Assert.Equal("{}", request.SettingsJson);
        Assert.Null(request.SocialAccountId);
        Assert.Null(request.Title);
    }
}
