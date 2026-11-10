using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Hashtags;
using Platform.Content.Features.Media;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Posts;

/// <summary>
/// Turns what a page sent into the post to store, checking every field on the way. Creating builds a new draft; editing builds the
/// edited copy of the post that exists (an edit sends the whole post, so what it leaves out is cleared, except the type, visibility,
/// language and time zone, which keep their value). All problems are collected in <paramref name="errors"/>; the caller throws them
/// together. Pure: nothing here reads a database or a clock.
/// </summary>
public static partial class PostBuilder
{
    public const int MaxTitleLength = 300;
    public const int MaxTags = 20;
    public const int MaxTagLength = 50;
    public const int MaxPlatforms = 8;
    public const int MaxCtas = 3;
    public const int MaxMentions = 20;
    public const int MaxKeywords = 50;
    public const int MaxJsonLength = 20_000;

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$")]
    private static partial Regex LanguageShape();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+/-]{0,63}$")]
    private static partial Regex TimezoneShape();

    public static ContentPostAggregate Build(
        SavePostRequest request, ContentPostAggregate? existing, Guid agencyId, Guid userId, DateTimeOffset now, ValidationErrors errors)
    {
        var creating = existing is null;
        var postId = existing?.Post.Id ?? Guid.NewGuid();

        var workspaceId = Guid.Empty;
        if (creating)
        {
            if (request.WorkspaceId is null || request.WorkspaceId == Guid.Empty)
            {
                errors.Add("workspaceId", "Choose a workspace.");
            }
            else
            {
                workspaceId = request.WorkspaceId.Value;
            }
        }
        else
        {
            workspaceId = existing!.Post.WorkspaceId;
            if (request.ExpectedUpdatedAt is null)
            {
                errors.Add("expectedUpdatedAt", "Say which version you are editing (expectedUpdatedAt).");
            }
        }

        var contentType = existing?.Post.ContentType ?? string.Empty;
        if (request.ContentType is null)
        {
            if (creating)
            {
                errors.Add("contentType", "Choose what you are creating.");
            }
        }
        else if (!DbEnum<ContentType>.TryParse(request.ContentType, out _))
        {
            errors.Add("contentType", $"Content type must be one of: {string.Join(", ", DbEnum<ContentType>.All)}.");
        }
        else
        {
            contentType = request.ContentType;
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length > MaxTitleLength)
        {
            errors.Add("title", $"Title must be at most {MaxTitleLength} characters.");
        }
        else if (title.Any(char.IsControl))
        {
            errors.Add("title", "Title cannot contain line breaks or control characters.");
        }

        string? bodyDoc = null;
        var body = string.Empty;
        var documentErrors = new ValidationErrors();
        RichTextDocument.Validate(request.BodyDoc, "bodyDoc", documentErrors);
        Merge(errors, documentErrors);
        if (!documentErrors.Any && request.BodyDoc is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } document)
        {
            bodyDoc = document.GetRawText();
            body = RichTextDocument.ToPlainText(document);
            if (body.Length > RichTextDocument.MaxTextLength)
            {
                errors.Add("bodyDoc", "The text is too long.");
            }
        }

        if (title.Length == 0 && body.Length == 0)
        {
            errors.Add("content", "Add a title or some text before saving.");
        }

        var visibility = existing?.Post.Visibility ?? ContentVisibility.Private.ToWire();
        if (request.Visibility is not null)
        {
            if (DbEnum<ContentVisibility>.TryParse(request.Visibility, out _))
            {
                visibility = request.Visibility;
            }
            else
            {
                errors.Add("visibility", $"Visibility must be one of: {string.Join(", ", DbEnum<ContentVisibility>.All)}.");
            }
        }

        var language = existing?.Post.LanguageCode ?? "en";
        if (request.LanguageCode is not null)
        {
            if (LanguageShape().IsMatch(request.LanguageCode))
            {
                language = request.LanguageCode;
            }
            else
            {
                errors.Add("languageCode", "Language must be a code such as en or pt-BR.");
            }
        }

        var timezone = existing?.Post.Timezone ?? "UTC";
        if (request.Timezone is not null)
        {
            if (IsKnownTimeZone(request.Timezone))
            {
                timezone = request.Timezone;
            }
            else
            {
                errors.Add("timezone", "Time zone must be a name such as Asia/Kolkata or UTC.");
            }
        }

        var (seoTitle, seoDescription, seoSlug, keywords) = BuildSeo(request, errors);
        var settings = BuildJsonObject(request.Settings, "settings", errors);

        var post = creating
            ? new ContentPost { Id = postId, AgencyId = agencyId, WorkspaceId = workspaceId, CreatedBy = userId, Status = ContentStatus.Draft.ToWire(), CreatedAt = now }
            : existing!.Post.Clone();
        post.Title = title;
        post.ContentType = contentType;
        post.Body = body;
        post.BodyDoc = bodyDoc;
        post.Excerpt = RichTextDocument.Excerpt(body);
        post.Visibility = visibility;
        post.LanguageCode = language;
        post.Timezone = timezone;
        post.CampaignId = request.CampaignId;
        post.SeoTitle = seoTitle;
        post.SeoDescription = seoDescription;
        post.SeoSlug = seoSlug;
        post.SeoKeywords = keywords;
        post.Settings = settings;
        post.UpdatedBy = userId;
        post.UpdatedAt = now;
        // A scheduled post keeps its schedule (it is changed by scheduling, not by editing); any other edit sets the date it is meant for.
        if (post.Status != ContentStatus.Scheduled.ToWire())
        {
            post.ScheduledAt = request.ScheduledAt;
        }

        var aggregate = new ContentPostAggregate { Post = post };
        var hashtags = HashtagNormalizer.NormalizeAll(request.Hashtags, errors, "hashtags");
        for (var i = 0; i < hashtags.Count; i++)
        {
            aggregate.Hashtags.Add(new ContentHashtag { Id = Guid.NewGuid(), AgencyId = agencyId, PostId = postId, Hashtag = hashtags[i], DisplayOrder = i });
        }

        foreach (var name in BuildTags(request.Tags, errors))
        {
            aggregate.Tags.Add(new ContentTag { AgencyId = agencyId, WorkspaceId = workspaceId, Name = name, Color = TagColors.For(name) });
        }

        BuildPlatforms(request.Platforms, aggregate, errors);
        BuildPoll(request.Poll, aggregate, now, errors);
        BuildCtas(request.Ctas, aggregate, errors);
        BuildMentions(request.Mentions, aggregate, errors);
        BuildLocation(request.Location, aggregate, errors);
        BuildMedia(request.Media, aggregate, errors);
        return aggregate;
    }

    private static bool IsKnownTimeZone(string id) =>
        TimezoneShape().IsMatch(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    private static void Merge(ValidationErrors into, ValidationErrors from)
    {
        foreach (var (field, messages) in from.ToDictionary())
        {
            foreach (var message in messages)
            {
                into.Add(field, message);
            }
        }
    }

    private static string? Optional(string? value, int maxLength, string field, string label, ValidationErrors errors, bool singleLine)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            errors.Add(field, $"{label} must be at most {maxLength} characters.");
            return null;
        }

        if (singleLine && trimmed.Any(char.IsControl))
        {
            errors.Add(field, $"{label} cannot contain line breaks or control characters.");
            return null;
        }

        return trimmed;
    }

    private static (string? Title, string? Description, string? Slug, string[] Keywords) BuildSeo(SavePostRequest request, ValidationErrors errors)
    {
        var title = Optional(request.SeoTitle, 200, "seoTitle", "SEO title", errors, singleLine: true);
        var description = Optional(request.SeoDescription, 500, "seoDescription", "SEO description", errors, singleLine: false);
        var slug = Optional(request.SeoSlug, 200, "seoSlug", "Slug", errors, singleLine: true);
        if (slug is not null && !SlugShape().IsMatch(slug))
        {
            errors.Add("seoSlug", "Slug can have lowercase letters, digits and single hyphens between them.");
            slug = null;
        }

        var keywords = new List<string>();
        foreach (var raw in request.SeoKeywords ?? [])
        {
            var keyword = raw?.Trim();
            if (string.IsNullOrEmpty(keyword) || keyword.Length > 100 || keyword.Any(char.IsControl))
            {
                errors.Add("seoKeywords", "A keyword is 1 to 100 characters on one line.");
            }
            else if (!keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(keyword);
            }
        }

        if (keywords.Count > MaxKeywords)
        {
            errors.Add("seoKeywords", $"At most {MaxKeywords} keywords.");
        }

        return (title, description, slug, [.. keywords]);
    }

    private static string BuildJsonObject(JsonElement? value, string field, ValidationErrors errors)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "{}";
        }

        if (value.Value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(field, "Settings must be an object.");
            return "{}";
        }

        var raw = value.Value.GetRawText();
        if (raw.Length > MaxJsonLength)
        {
            errors.Add(field, "Settings are too large.");
            return "{}";
        }

        return raw;
    }

    private static List<string> BuildTags(string[]? names, ValidationErrors errors)
    {
        var result = new List<string>();
        foreach (var raw in names ?? [])
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxTagLength || name.Any(char.IsControl))
            {
                errors.Add("tags", $"A tag is 1 to {MaxTagLength} characters on one line.");
            }
            else if (!result.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        if (result.Count > MaxTags)
        {
            errors.Add("tags", $"A post can have at most {MaxTags} tags.");
        }

        return result;
    }

    private static void BuildPlatforms(List<PlatformTargetInput>? targets, ContentPostAggregate aggregate, ValidationErrors errors)
    {
        var post = aggregate.Post;
        var scheduled = post.Status == ContentStatus.Scheduled.ToWire();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (targets is { Count: > MaxPlatforms })
        {
            errors.Add("platforms", $"At most {MaxPlatforms} platforms.");
            return;
        }

        foreach (var target in targets ?? [])
        {
            if (target is null || !DbEnum<SocialPlatform>.TryParse(target.Platform, out _))
            {
                errors.Add("platforms", $"Platform must be one of: {string.Join(", ", DbEnum<SocialPlatform>.All)}.");
                continue;
            }

            if (!seen.Add(target.Platform!))
            {
                errors.Add("platforms", $"{target.Platform} is listed twice.");
                continue;
            }

            var caption = string.IsNullOrWhiteSpace(target.Caption) ? null : target.Caption.Trim();
            if (caption is { Length: > 20_000 })
            {
                errors.Add("platforms", "A platform's own text is too long.");
            }

            var title = Optional(target.Title, MaxTitleLength, "platforms", "A platform's title", errors, singleLine: true);
            aggregate.Platforms.Add(new ContentPostPlatform
            {
                Id = Guid.NewGuid(),
                AgencyId = post.AgencyId,
                PostId = post.Id,
                Platform = target.Platform!,
                PlatformCaption = caption,
                PlatformTitle = title,
                PlatformSettings = BuildJsonObject(target.Settings, "platforms", errors),
                PublishStatus = (scheduled ? PublishStatus.Scheduled : PublishStatus.Pending).ToWire(),
                ScheduledAt = scheduled ? post.ScheduledAt : null
            });
        }
    }

    private static void BuildPoll(PollInput? poll, ContentPostAggregate aggregate, DateTimeOffset now, ValidationErrors errors)
    {
        if (poll is null)
        {
            return;
        }

        var question = poll.Question?.Trim() ?? string.Empty;
        if (question.Length is < 1 or > 300 || question.Any(char.IsControl))
        {
            errors.Add("poll", "A poll needs a question of up to 300 characters on one line.");
        }

        var options = new List<string>();
        foreach (var raw in poll.Options ?? [])
        {
            var option = raw?.Trim() ?? string.Empty;
            if (option.Length is < 1 or > 100 || option.Any(char.IsControl))
            {
                errors.Add("poll", "A poll option is 1 to 100 characters on one line.");
            }
            else if (options.Contains(option, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("poll", "A poll cannot offer the same option twice.");
            }
            else
            {
                options.Add(option);
            }
        }

        if (options.Count is < 2 or > 4)
        {
            errors.Add("poll", "A poll has two to four options.");
        }

        if (poll.EndsAt is not null && poll.EndsAt <= now)
        {
            errors.Add("poll", "A poll must end in the future.");
        }

        var post = aggregate.Post;
        var pollId = Guid.NewGuid();
        aggregate.Poll = new ContentPoll { Id = pollId, AgencyId = post.AgencyId, PostId = post.Id, Question = question, EndsAt = poll.EndsAt };
        for (var i = 0; i < options.Count; i++)
        {
            aggregate.PollOptions.Add(new ContentPollOption { Id = Guid.NewGuid(), AgencyId = post.AgencyId, PollId = pollId, OptionText = options[i], DisplayOrder = i });
        }
    }

    private static void BuildCtas(List<CtaInput>? ctas, ContentPostAggregate aggregate, ValidationErrors errors)
    {
        if (ctas is { Count: > MaxCtas })
        {
            errors.Add("ctas", $"At most {MaxCtas} buttons.");
            return;
        }

        var post = aggregate.Post;
        foreach (var cta in ctas ?? [])
        {
            var text = cta?.ButtonText?.Trim() ?? string.Empty;
            var url = cta?.TargetUrl?.Trim() ?? string.Empty;
            if (text.Length is < 1 or > 60 || text.Any(char.IsControl))
            {
                errors.Add("ctas", "A button's text is 1 to 60 characters on one line.");
            }
            else if (!IsWebAddress(url))
            {
                errors.Add("ctas", "A button's address must be a web address starting with http:// or https://.");
            }
            else
            {
                aggregate.Ctas.Add(new ContentCta
                {
                    Id = Guid.NewGuid(), AgencyId = post.AgencyId, PostId = post.Id, ButtonText = text, TargetUrl = url, DisplayOrder = aggregate.Ctas.Count
                });
            }
        }
    }

    /// <summary>An absolute http or https address, no spaces or control characters, at most 2048 characters.</summary>
    public static bool IsWebAddress(string? url) =>
        !string.IsNullOrEmpty(url) && url.Length <= 2048 && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);

    private static void BuildMentions(List<MentionInput>? mentions, ContentPostAggregate aggregate, ValidationErrors errors)
    {
        if (mentions is { Count: > MaxMentions })
        {
            errors.Add("mentions", $"At most {MaxMentions} people.");
            return;
        }

        var post = aggregate.Post;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mention in mentions ?? [])
        {
            var name = mention?.Name?.Trim() ?? string.Empty;
            if (mention is null || !DbEnum<SocialPlatform>.TryParse(mention.Platform, out _))
            {
                errors.Add("mentions", $"A mention's platform must be one of: {string.Join(", ", DbEnum<SocialPlatform>.All)}.");
            }
            else if (name.Length is < 1 or > 200 || name.Any(char.IsControl))
            {
                errors.Add("mentions", "A mention's name is 1 to 200 characters on one line.");
            }
            else if (seen.Add($"{mention.Platform}:{name}"))
            {
                aggregate.Mentions.Add(new ContentMention { Id = Guid.NewGuid(), AgencyId = post.AgencyId, PostId = post.Id, Platform = mention.Platform!, MentionedName = name });
            }
        }
    }

    /// <summary>
    /// The files of the library on the post: how many, that each is named once, and its caption. Whether a file exists, is in this
    /// workspace and is an image or a video needs the library, so the service checks that (and fixes each file's role) afterwards.
    /// </summary>
    private static void BuildMedia(List<MediaInput>? media, ContentPostAggregate aggregate, ValidationErrors errors)
    {
        if (media is { Count: > MediaPolicy.MaxMedia })
        {
            errors.Add("media", $"At most {MediaPolicy.MaxMedia} files.");
            return;
        }

        var post = aggregate.Post;
        var seen = new HashSet<Guid>();
        foreach (var item in media ?? [])
        {
            if (item?.AssetId is null || item.AssetId == Guid.Empty)
            {
                errors.Add("media", "A file needs its id.");
                continue;
            }

            if (!seen.Add(item.AssetId.Value))
            {
                errors.Add("media", "The same file is on the post twice.");
                continue;
            }

            var caption = item.Caption?.Trim() ?? string.Empty;
            if (caption.Length > MediaPolicy.MaxCaptionLength)
            {
                errors.Add("media", $"A caption is at most {MediaPolicy.MaxCaptionLength} characters.");
                continue;
            }

            aggregate.Media.Add(new ContentPostMedia
            {
                Id = Guid.NewGuid(), AgencyId = post.AgencyId, PostId = post.Id, MediaAssetId = item.AssetId.Value,
                DisplayOrder = aggregate.Media.Count, MediaRole = MediaRole.Attachment.ToWire(), Caption = caption
            });
        }
    }

    private static void BuildLocation(LocationInput? location, ContentPostAggregate aggregate, ValidationErrors errors)
    {
        if (location is null)
        {
            return;
        }

        var name = location.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 200 || name.Any(char.IsControl))
        {
            errors.Add("location", "A location's name is 1 to 200 characters on one line.");
        }

        if ((location.Latitude is null) != (location.Longitude is null))
        {
            errors.Add("location", "Give both the latitude and the longitude, or neither.");
        }
        else if (location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180)
        {
            errors.Add("location", "Latitude is from -90 to 90 and longitude from -180 to 180.");
        }

        var post = aggregate.Post;
        aggregate.Location = new ContentLocation
        {
            Id = Guid.NewGuid(), AgencyId = post.AgencyId, PostId = post.Id, LocationName = name,
            Latitude = location.Latitude is null ? null : Math.Round(location.Latitude.Value, 6),
            Longitude = location.Longitude is null ? null : Math.Round(location.Longitude.Value, 6)
        };
    }
}
