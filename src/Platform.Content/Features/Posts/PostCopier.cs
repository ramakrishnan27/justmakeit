using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Data;

namespace Platform.Content.Features.Posts;

/// <summary>A new draft that says what an existing post says, for "Duplicate". Pure.</summary>
public static class PostCopier
{
    private const string Prefix = "Copy of ";

    public static ContentPostAggregate Copy(ContentPostAggregate source, Guid userId, DateTimeOffset now)
    {
        var from = source.Post;
        var id = Guid.NewGuid();
        var title = from.Title.Length == 0 ? string.Empty : Prefix + from.Title;
        if (title.Length > PostBuilder.MaxTitleLength)
        {
            title = title[..PostBuilder.MaxTitleLength];
        }

        var post = from.Clone();
        post.Id = id;
        post.Title = title;
        post.Status = ContentStatus.Draft.ToWire();
        post.CreatedBy = userId;
        post.UpdatedBy = userId;
        post.ScheduledAt = null;
        post.PublishedAt = null;
        post.ArchivedAt = null;
        // A copy keeps its own place in the list, not the original's slug (two posts of a workspace cannot share one).
        post.SeoSlug = null;
        post.CreatedAt = now;
        post.UpdatedAt = now;

        var copy = new ContentPostAggregate { Post = post };
        var agencyId = from.AgencyId;
        copy.Hashtags.AddRange(source.Hashtags.Select(h => new ContentHashtag { Id = Guid.NewGuid(), AgencyId = agencyId, PostId = id, Hashtag = h.Hashtag, DisplayOrder = h.DisplayOrder }));
        copy.Tags.AddRange(source.Tags.Select(t => new ContentTag { AgencyId = agencyId, WorkspaceId = t.WorkspaceId, Name = t.Name, Color = t.Color }));
        copy.Platforms.AddRange(source.Platforms.Select(p => new ContentPostPlatform
        {
            Id = Guid.NewGuid(),
            AgencyId = agencyId,
            PostId = id,
            SocialAccountId = p.SocialAccountId,
            Platform = p.Platform,
            PlatformCaption = p.PlatformCaption,
            PlatformTitle = p.PlatformTitle,
            PlatformSettings = p.PlatformSettings,
            PublishStatus = PublishStatus.Pending.ToWire()
        }));

        if (source.Poll is { } poll)
        {
            var pollId = Guid.NewGuid();
            copy.Poll = new ContentPoll { Id = pollId, AgencyId = agencyId, PostId = id, Question = poll.Question, EndsAt = null };
            copy.PollOptions.AddRange(source.PollOptions.Select(o => new ContentPollOption { Id = Guid.NewGuid(), AgencyId = agencyId, PollId = pollId, OptionText = o.OptionText, DisplayOrder = o.DisplayOrder }));
        }

        copy.Ctas.AddRange(source.Ctas.Select(c => new ContentCta { Id = Guid.NewGuid(), AgencyId = agencyId, PostId = id, ButtonText = c.ButtonText, TargetUrl = c.TargetUrl, DisplayOrder = c.DisplayOrder }));
        copy.Mentions.AddRange(source.Mentions.Select(m => new ContentMention { Id = Guid.NewGuid(), AgencyId = agencyId, PostId = id, Platform = m.Platform, MentionedName = m.MentionedName, PlatformUserId = m.PlatformUserId }));
        copy.Media.AddRange(source.Media.Select(m => new ContentPostMedia
        {
            Id = Guid.NewGuid(), AgencyId = agencyId, PostId = id, MediaAssetId = m.MediaAssetId, DisplayOrder = m.DisplayOrder, MediaRole = m.MediaRole, Caption = m.Caption
        }));

        if (source.Location is { } location)
        {
            copy.Location = new ContentLocation
            {
                Id = Guid.NewGuid(), AgencyId = agencyId, PostId = id, LocationName = location.LocationName,
                Latitude = location.Latitude, Longitude = location.Longitude, ExternalPlaceId = location.ExternalPlaceId
            };
        }

        return copy;
    }
}
