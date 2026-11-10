using Microsoft.Extensions.Options;
using Platform.Content.Data;
using Platform.Content.Enums;
using Platform.Content.Interfaces;
using Platform.Content.Settings;
using Platform.Helpers.Publishing;

namespace Platform.Content.Features.Publishing;

/// <summary>
/// The publisher for each platform: the real ones the channel projects registered, and, when the development fake is switched on,
/// the fake for every platform that has no real one. Built once, when the options are first read, so a test can switch the fake on.
/// </summary>
public sealed class PublisherCatalog : IPublisherCatalog
{
    private readonly Dictionary<string, ISocialPublisher> _publishers = new(StringComparer.Ordinal);

    public PublisherCatalog(IEnumerable<ISocialPublisher> real, IOptions<ContentOptions> options)
    {
        foreach (var publisher in real)
        {
            // One per platform: the first registered wins, so a second registration cannot silently replace a working channel.
            _publishers.TryAdd(publisher.Platform, publisher);
        }

        var hasReal = _publishers.Count > 0;
        var fake = options.Value.Publishing.UseFakePublisher;
        if (fake)
        {
            foreach (var platform in DbEnum<SocialPlatform>.All)
            {
                _publishers.TryAdd(platform, new FakeSocialPublisher(platform));
            }
        }

        Mode = hasReal ? "live" : fake ? "fake" : "none";
    }

    public string Mode { get; }

    public ISocialPublisher? Find(string platform) => _publishers.GetValueOrDefault(platform);
}
