namespace Platform.Helpers.Publishing;

/// <summary>
/// What a social channel (Facebook, Instagram, YouTube, ...) offers Content Studio: publish one piece of content to its platform.
/// The contract lives in Core so that the content feature and every channel project depend on Core only: a channel never needs to
/// reference the content feature, and the content feature never references a channel. Platform.Api registers the
/// implementations; the content feature picks the one whose <see cref="Platform"/> matches the target.
/// </summary>
public interface ISocialPublisher
{
    /// <summary>
    /// The platform this publisher delivers to, lower case and exactly as the database stores it
    /// (<c>content_post_platforms.platform</c>: facebook, instagram, x, youtube, linkedin, tiktok, pinterest, threads).
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Publishes the request. Expected failures (the platform refused, the account is not connected, a rate limit) are
    /// returned as <see cref="SocialPublishResult.Failure"/>, not thrown, so the caller can record them and decide on a retry.
    /// <see cref="SocialPublishRequest.PublicationId"/> is stable across retries of the same target: use it to avoid posting twice.
    /// </summary>
    Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken cancellationToken);
}
