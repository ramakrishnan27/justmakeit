using Platform.Helpers.Publishing;

namespace Platform.Content.Features.Publishing;

/// <summary>
/// A stand-in for a platform's publisher, for development and demonstrations: it posts nothing anywhere and answers at once. It is
/// only ever created when <c>Content:Publishing:UseFakePublisher</c> is on. Text containing <c>[fail]</c> makes it fail for good and
/// <c>[retry]</c> makes it fail in a way that is worth trying again, so both paths can be seen without a real platform.
/// </summary>
public sealed class FakeSocialPublisher(string platform) : ISocialPublisher
{
    public const string FailMarker = "[fail]";
    public const string RetryMarker = "[retry]";

    public string Platform { get; } = platform;

    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken cancellationToken)
    {
        if (request.Text.Contains(RetryMarker, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(SocialPublishResult.Failure("The fake publisher was asked to fail, and to be tried again.", retryable: true));
        }

        if (request.Text.Contains(FailMarker, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(SocialPublishResult.Failure("The fake publisher was asked to fail."));
        }

        var id = request.PublicationId.ToString("N");
        return Task.FromResult(SocialPublishResult.Success($"fake-{id}", $"https://example.com/fake/{Platform}/{id}"));
    }
}
