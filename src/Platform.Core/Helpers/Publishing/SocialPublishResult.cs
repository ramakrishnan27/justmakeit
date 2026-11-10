namespace Platform.Helpers.Publishing;

/// <summary>
/// The outcome of one publish attempt. Build it with <see cref="Success"/> or <see cref="Failure"/>. The error text is shown to
/// people and stored: never put a token, a secret or a raw response body in it.
/// </summary>
public sealed class SocialPublishResult
{
    private SocialPublishResult()
    {
    }

    public bool Succeeded { get; private init; }

    /// <summary>The platform's id for the post. Set when <see cref="Succeeded"/>.</summary>
    public string? ExternalPostId { get; private init; }

    /// <summary>Where the post can be seen, an http(s) address, when the platform gave one.</summary>
    public string? ExternalPostUrl { get; private init; }

    /// <summary>Why it failed, in words a person can act on. Set when not <see cref="Succeeded"/>.</summary>
    public string? Error { get; private init; }

    /// <summary>Whether trying again later may work (a rate limit, a timeout) as opposed to never (the account is not connected, the platform refused the content).</summary>
    public bool Retryable { get; private init; }

    public static SocialPublishResult Success(string externalPostId, string? externalPostUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalPostId);
        return new SocialPublishResult { Succeeded = true, ExternalPostId = externalPostId, ExternalPostUrl = externalPostUrl };
    }

    public static SocialPublishResult Failure(string error, bool retryable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new SocialPublishResult { Succeeded = false, Error = error, Retryable = retryable };
    }
}
