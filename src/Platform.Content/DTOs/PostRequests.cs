using System.Text.Json;
using Platform.Content.Data;
using Platform.Content.Enums;
using Platform.Helpers.Validation;

namespace Platform.Content.DTOs;

/// <summary>
/// What the Create and Edit pages send to save a post. Every reference property is nullable so a missing field is reported as a field
/// error by the builder, in the API's own problem format, instead of by model binding. The plain text of the post is never sent: the
/// server derives it from <see cref="BodyDoc"/>.
/// </summary>
public sealed class SavePostRequest
{
    /// <summary>The workspace the post belongs to. Required when creating; ignored when editing (a post stays in its workspace).</summary>
    public Guid? WorkspaceId { get; init; }

    /// <summary>One of social_post, blog_post, video_script, carousel, short_reel. Required when creating.</summary>
    public string? ContentType { get; init; }

    public string? Title { get; init; }

    /// <summary>The editor's document (a ProseMirror JSON object). Checked against an allowlist of node and mark types.</summary>
    public JsonElement? BodyDoc { get; init; }

    /// <summary>public, private or unlisted. Defaults to private.</summary>
    public string? Visibility { get; init; }

    public string? LanguageCode { get; init; }

    /// <summary>The IANA time zone the person schedules in, for showing the time back to them.</summary>
    public string? Timezone { get; init; }

    /// <summary>The date a draft is meant to go out. It becomes the schedule only through the schedule endpoint.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }

    public Guid? CampaignId { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDescription { get; init; }
    public string? SeoSlug { get; init; }
    public string[]? SeoKeywords { get; init; }

    /// <summary>With or without the leading #.</summary>
    public string[]? Hashtags { get; init; }

    /// <summary>Tag names. A name the workspace does not have yet creates the tag.</summary>
    public string[]? Tags { get; init; }

    public List<PlatformTargetInput>? Platforms { get; init; }
    public PollInput? Poll { get; init; }
    public List<CtaInput>? Ctas { get; init; }
    public List<MentionInput>? Mentions { get; init; }
    public LocationInput? Location { get; init; }

    /// <summary>The files of the Media Library on the post, in order. The whole list is sent on every save; what it leaves out is removed.</summary>
    public List<MediaInput>? Media { get; init; }

    public JsonElement? Settings { get; init; }

    /// <summary>The <c>updatedAt</c> the page loaded. Required when editing: a different value means someone else saved first (409).</summary>
    public DateTimeOffset? ExpectedUpdatedAt { get; init; }
}

/// <summary>One platform a post goes to, with what is different about it there.</summary>
public sealed class PlatformTargetInput
{
    public string? Platform { get; init; }

    /// <summary>Null or empty publishes the post's own text on this platform.</summary>
    public string? Caption { get; init; }

    public string? Title { get; init; }
    public JsonElement? Settings { get; init; }
}

public sealed class PollInput
{
    public string? Question { get; init; }
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>Two to four.</summary>
    public string[]? Options { get; init; }
}

public sealed class CtaInput
{
    public string? ButtonText { get; init; }
    public string? TargetUrl { get; init; }
}

public sealed class MentionInput
{
    public string? Platform { get; init; }
    public string? Name { get; init; }
}

/// <summary>One file of the Media Library on a post. The role is never sent: the server works it out from the kind of content.</summary>
public sealed class MediaInput
{
    public Guid? AssetId { get; init; }

    /// <summary>Sent back as received; the page has no way to edit it yet.</summary>
    public string? Caption { get; init; }
}

public sealed class LocationInput
{
    public string? Name { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}

/// <summary>Schedule a post to go out at a moment. At least a minute from now, at most a year.</summary>
public sealed record ScheduleRequest(DateTimeOffset? ScheduledAt);

/// <summary>
/// The list page's filters, from the query string. Everything is optional; <c>type</c> picks a tab, and the per-tab counts the answer
/// carries ignore it so the tabs keep their numbers while one is open.
/// </summary>
public sealed record PostListQuery(
    Guid? WorkspaceId = null,
    string? Type = null,
    string? Status = null,
    string? Platform = null,
    Guid? CampaignId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Q = null,
    int Page = 1,
    int PageSize = PostListQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (Page < 1)
        {
            errors.Add("page", "Page must be at least 1.");
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (Type is not null && !DbEnum<ContentType>.TryParse(Type, out _))
        {
            errors.Add("type", $"Type must be one of: {string.Join(", ", DbEnum<ContentType>.All)}.");
        }

        if (Status is not null && !DbEnum<ContentStatus>.TryParse(Status, out _))
        {
            errors.Add("status", $"Status must be one of: {string.Join(", ", DbEnum<ContentStatus>.All)}.");
        }

        if (Platform is not null && !DbEnum<SocialPlatform>.TryParse(Platform, out _))
        {
            errors.Add("platform", $"Platform must be one of: {string.Join(", ", DbEnum<SocialPlatform>.All)}.");
        }

        if (From is not null && To is not null && From >= To)
        {
            errors.Add("to", "The end of the date range must be after its start.");
        }

        if (Q is { Length: > MaxSearchLength })
        {
            errors.Add("q", $"Search must be at most {MaxSearchLength} characters.");
        }

        return errors;
    }
}
